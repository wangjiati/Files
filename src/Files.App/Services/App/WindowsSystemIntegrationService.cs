// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Files.App.Services
{
	internal sealed class WindowsSystemIntegrationService : ISystemIntegrationService
	{
		private const string StartupTaskId = "3AA55462-A5FA-4933-88C4-712D0B6CDEBB";

		public async Task<StartupTaskState> GetStartupTaskStateAsync()
		{
			try
			{
				var state = await StartupTask.GetAsync(StartupTaskId);
				return state.State;
			}
			catch (COMException ex)
			{
				App.Logger?.LogWarning(ex, "RPC server unavailable, returning default state");
				return StartupTaskState.Disabled;
			}
		}

		public async Task SetStartupTaskEnabledAsync(bool enabled)
		{
			try
			{
				var startupTask = await StartupTask.GetAsync(StartupTaskId);
				if (enabled)
					await startupTask.RequestEnableAsync();
				else
					startupTask.Disable();
			}
			catch (COMException ex)
			{
				App.Logger?.LogWarning(ex, "RPC server unavailable, returning default state");
			}
		}

		public bool IsDefaultFileManager()
		{
			using var subkey = Registry.ClassesRoot.OpenSubKey(@"Folder\shell\open\command");
			var command = (string?)subkey?.GetValue(string.Empty);
			return !string.IsNullOrEmpty(command) && command.Contains("Files.App.Launcher.exe");
		}

		public async Task SetDefaultFileManagerAsync(bool enabled)
		{
			var destFolder = Path.Combine(AppRuntime.Current.DataDirectory, "FilesOpenDialog");
			Directory.CreateDirectory(destFolder);
			foreach (var file in Directory.GetFiles(Path.Combine(AppRuntime.Current.InstallDirectory, "Assets", "FilesOpenDialog")))
			{
				if (!SafetyExtensions.IgnoreExceptions(() => File.Copy(file, Path.Combine(destFolder, Path.GetFileName(file)), true), App.Logger))
					return;
			}

			var dataPath = Environment.ExpandEnvironmentVariables("%LocalAppData%\\Files");
			if (enabled)
			{
				var sourcePath = Path.Combine(destFolder, "*");
				var branchPath = Path.Combine(dataPath, "Branch.txt");
				var command = $"-command \"New-Item -Force -Path {Win32Helper.ToPowerShellStringLiteral(dataPath)} -ItemType Directory; Copy-Item -Filter *.* -Path {Win32Helper.ToPowerShellStringLiteral(sourcePath)} -Recurse -Force -Destination {Win32Helper.ToPowerShellStringLiteral(dataPath)}; 'files-internal' | Out-File -Encoding utf8 -Force -FilePath {Win32Helper.ToPowerShellStringLiteral(branchPath)}\"";
				if (!await Win32Helper.RunPowershellCommandAsync(command, PowerShellExecutionOptions.Hidden))
					return;
			}
			else
			{
				await Win32Helper.RunPowershellCommandAsync($"-command \"Remove-Item -LiteralPath {Win32Helper.ToPowerShellStringLiteral(dataPath)} -Recurse -Force\"", PowerShellExecutionOptions.Hidden);
			}

			try
			{
				using var regProcess = Process.Start(new ProcessStartInfo("regedit.exe", @$"/s ""{Path.Combine(destFolder, enabled ? "SetFilesAsDefault.reg" : "UnsetFilesAsDefault.reg")}""") { UseShellExecute = true, Verb = "runas" });
				if (regProcess is not null)
					await regProcess.WaitForExitAsync();
			}
			catch
			{
			}
		}

		public bool IsOpenFileDialog()
		{
			using var subkeyOpen = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\CLSID\{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}");
			using var subkeySave = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\CLSID\{C0B4E2F3-BA21-4773-8DBA-335EC946EB8B}");
			var isSetAsOpenDialog = subkeyOpen?.GetValue(string.Empty) as string == "FilesOpenDialog class";
			var isSetAsSaveDialog = subkeySave?.GetValue(string.Empty) as string == "FilesSaveDialog class";
			return isSetAsOpenDialog || isSetAsSaveDialog;
		}

		public async Task SetOpenFileDialogAsync(bool enabled)
		{
			var destFolder = Path.Combine(AppRuntime.Current.DataDirectory, "FilesOpenDialog");
			Directory.CreateDirectory(destFolder);
			foreach (var file in Directory.GetFiles(Path.Combine(AppRuntime.Current.InstallDirectory, "Assets", "FilesOpenDialog")))
			{
				if (!SafetyExtensions.IgnoreExceptions(() => File.Copy(file, Path.Combine(destFolder, Path.GetFileName(file)), true), App.Logger))
					return;
			}

			try
			{
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.OpenDialog32.dll")}"""))
					await regProc.WaitForExitAsync();
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.OpenDialog64.dll")}"""))
					await regProc.WaitForExitAsync();
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.OpenDialogARM64.dll")}"""))
					await regProc.WaitForExitAsync();
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.SaveDialog32.dll")}"""))
					await regProc.WaitForExitAsync();
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.SaveDialog64.dll")}"""))
					await regProc.WaitForExitAsync();
				using (var regProc = Process.Start("regsvr32.exe", @$"/s /n {(!enabled ? "/u" : "")} /i:user ""{Path.Combine(destFolder, "Files.App.SaveDialogARM64.dll")}"""))
					await regProc.WaitForExitAsync();
			}
			catch
			{
			}
		}
	}
}
