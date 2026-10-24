// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace Files.App.Services
{
	internal sealed class WindowsOpenDialogRegistration : IOpenDialogRegistration
	{
		private static readonly string[] Architectures = ["32", "64", "ARM64"];

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
			foreach (var architecture in Architectures)
			{
				var libraryPath = Path.Combine(integrationDirectory, $"{libraryName}{architecture}.dll");
				if (!File.Exists(libraryPath))
					continue;

				try
				{
					using var process = Process.Start(
						"regsvr32.exe",
						$"/s /n {(register ? string.Empty : "/u")} /i:user \"{libraryPath}\"");
					process?.WaitForExit();
				}
				catch (Exception ex)
				{
					App.Logger?.LogWarning(ex, "Unable to update the open/save dialog registration.");
				}
			}
		}
	}
}
