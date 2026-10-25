// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Files.App.Helpers
{
	internal static class AppProcessLauncher
	{
		internal const string NewWindowArgument = "--new-window";
		internal const string ProtocolScheme = "files-internal";

		public static bool SupportsNewWindows(DistributionKind distribution) => distribution is DistributionKind.Msix;

		public static Task<bool> LaunchNewWindowAsync(string? query = null)
		{
			if (!SupportsNewWindows(AppRuntime.Current.Distribution) || Environment.ProcessPath is not { } processPath)
				return Task.FromResult(false);

			var arguments = $"{NewWindowArgument} {ProtocolScheme}:?{query}";
			return Task.FromResult(Start(processPath, arguments));
		}

		public static Task<bool> RelaunchAsync()
		{
			if (Environment.ProcessPath is not { } processPath)
				return Task.FromResult(false);

			return RelaunchAsync(processPath, Start, Program.ReleasePortableDataLease, Program.RestorePortableDataLease);
		}

		internal static Task<bool> RelaunchAsync(
			string processPath,
			Func<string, string, bool> start,
			Action releasePortableDataLease,
			Action restorePortableDataLease)
		{
			try
			{
				releasePortableDataLease();
			}
			catch (Exception ex)
			{
				App.Logger?.LogError(ex, "Unable to release the portable data lease before restarting.");
				return Task.FromResult(false);
			}

			if (start(processPath, string.Empty))
				return Task.FromResult(true);

			try
			{
				restorePortableDataLease();
			}
			catch (Exception ex)
			{
				// The caller reports the failed restart; never let lease recovery escape an async void handler.
				App.Logger?.LogError(ex, "Unable to reacquire the portable data lease after a failed restart.");
			}

			return Task.FromResult(false);
		}

		internal static ProcessStartInfo CreateStartInfo(string processPath, string arguments) => new(processPath, arguments)
		{
			UseShellExecute = false,
		};

		private static bool Start(string processPath, string arguments)
		{
			try
			{
				return Process.Start(CreateStartInfo(processPath, arguments)) is not null;
			}
			catch
			{
				return false;
			}
		}
	}
}
