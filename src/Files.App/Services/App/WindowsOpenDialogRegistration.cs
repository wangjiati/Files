// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.App.Services
{
	internal sealed class WindowsOpenDialogRegistration : IOpenDialogRegistration
	{
		private static readonly string[] Architectures = ["32", "64", "ARM64"];
		private static readonly string[] RequiredArchitectures =
			RuntimeInformation.ProcessArchitecture == Architecture.Arm64
				? ["32", "ARM64"]
				: ["32", "64"];

		public void RegisterOpenDialog(string integrationDirectory)
			=> RunRegSvr32(integrationDirectory, "Files.App.OpenDialog", register: true);

		public void RegisterSaveDialog(string integrationDirectory)
			=> RunRegSvr32(integrationDirectory, "Files.App.SaveDialog", register: true);

		public void UnregisterOpenDialog(string integrationDirectory)
			=> RunRegSvr32(integrationDirectory, "Files.App.OpenDialog", register: false);

		public void UnregisterSaveDialog(string integrationDirectory)
			=> RunRegSvr32(integrationDirectory, "Files.App.SaveDialog", register: false);

		private static void RunRegSvr32(string integrationDirectory, string libraryName, bool register)
		{
			foreach (var architecture in RequiredArchitectures)
			{
				var libraryPath = Path.Combine(integrationDirectory, $"{libraryName}{architecture}.dll");
				if (!File.Exists(libraryPath))
					throw new FileNotFoundException($"The dialog registration library '{libraryPath}' was not found.");
			}

			foreach (var architecture in Architectures)
			{
				var libraryPath = Path.Combine(integrationDirectory, $"{libraryName}{architecture}.dll");
				if (!File.Exists(libraryPath))
					continue;

				using var process = Process.Start(
					"regsvr32.exe",
					$"/s /n {(register ? string.Empty : "/u")} /i:user \"{libraryPath}\"")
					?? throw new InvalidOperationException($"regsvr32.exe could not be started for '{libraryPath}'.");

				if (!process.WaitForExit(30000))
				{
					process.Kill();
					throw new TimeoutException($"regsvr32.exe timed out while updating '{libraryPath}'.");
				}

				if (process.ExitCode != 0)
					throw new InvalidOperationException($"regsvr32.exe failed with exit code {process.ExitCode} for '{libraryPath}'.");
			}
		}
	}
}
