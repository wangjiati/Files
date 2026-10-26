// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Ties one directory filter projection to the directory, spec, source revision and generation
	/// it was started for, so a stale background evaluation can never replace newer results.
	/// </summary>
	public sealed class DirectoryFilterExecutionContext : IDisposable
	{
		private readonly CancellationTokenSource cancellation = new();

		public DirectoryFilterExecutionContext(string directoryKey, DirectoryFilterSpec spec, long sourceRevision, long generation)
		{
			DirectoryKey = directoryKey ?? string.Empty;
			Spec = spec;
			SourceRevision = sourceRevision;
			Generation = generation;
		}

		public string DirectoryKey { get; }

		public DirectoryFilterSpec Spec { get; }

		public long SourceRevision { get; }

		public long Generation { get; }

		public bool IsCancellationRequested
			=> cancellation.IsCancellationRequested;

		public CancellationToken Token
			=> cancellation.Token;

		public CancellationTokenSource CreateLinkedTokenSource(CancellationToken token)
			=> CancellationTokenSource.CreateLinkedTokenSource(token, cancellation.Token);

		/// <summary>
		/// Returns whether these values still describe the projection this context was started for.
		/// The spec is compared by instance because every filter edit produces a new immutable spec.
		/// </summary>
		public bool CanCommit(string directoryKey, DirectoryFilterSpec spec, long sourceRevision, long generation)
			=> !cancellation.IsCancellationRequested
				&& generation == Generation
				&& sourceRevision == SourceRevision
				&& string.Equals(directoryKey ?? string.Empty, DirectoryKey, StringComparison.OrdinalIgnoreCase)
				&& ReferenceEquals(spec, Spec);

		public void Cancel()
		{
			try
			{
				cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// A completed projection already released the context.
			}
		}

		public void Dispose()
		{
			cancellation.Dispose();
		}
	}
}
