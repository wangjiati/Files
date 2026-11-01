// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// One rename to execute: the full source path, the full destination path, and the
	/// conflict decision the destination name was produced under.
	/// </summary>
	public sealed record RenameExecutionItem(string SourcePath, string DestinationPath, ConflictStatus Status);

	/// <summary>
	/// Result of executing a rename plan: the paths that failed and why, in execution order.
	/// </summary>
	public sealed record RenameExecutionResult(IReadOnlyList<(string Path, string Error)> Failed);

	/// <summary>
	/// Executes a built rename plan against the file system.
	/// </summary>
	/// <remarks>
	/// Single item failures are collected in the result, not thrown; execution continues with
	/// the remaining items.
	/// </remarks>
	public interface IRenameExecutor
	{
		/// <summary>
		/// Executes every item of the plan and reports the failures that occurred.
		/// </summary>
		/// <param name="plan">The plan, in the order the renames must run.</param>
		/// <param name="fs">The file system the renames run against.</param>
		/// <param name="progress">Receives a short progress message per item, if given.</param>
		/// <param name="ct">Cancels the remaining items.</param>
		Task<RenameExecutionResult> ExecuteAsync(IReadOnlyList<RenameExecutionItem> plan, IFilesystemHelpers fs, IProgress<string>? progress, CancellationToken ct);
	}
}
