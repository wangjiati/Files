// Copyright (c) Files Community
// Licensed under the MIT License.

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

			Program.ReleasePortableDataLease();
			return Task.FromResult(Start(processPath, string.Empty));
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
