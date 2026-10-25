// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Files.App.Services
{
	internal sealed class WindowsSystemIntegrationService : ISystemIntegrationService
	{
		private const string StartupTaskId = "3AA55462-A5FA-4933-88C4-712D0B6CDEBB";

		private readonly ShellIntegrationPaths _integrationPaths = ShellIntegrationPaths.CreateDefault();
		private readonly ShellIntegrationTakeover _takeover;

		public WindowsSystemIntegrationService()
		{
			_takeover = new ShellIntegrationTakeover(
				new WindowsShellIntegrationRegistry(),
				AppRuntime.LocalState,
				_integrationPaths,
				new WindowsOpenDialogRegistration());
		}

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
			=> GetDefaultFileManagerOwner() is ShellIntegrationOwner.Internal;

		public ShellIntegrationOwner GetDefaultFileManagerOwner()
			=> _takeover.GetDefaultFileManagerOwner();

		public async Task SetDefaultFileManagerAsync(bool enabled)
		{
			if (enabled)
				CopyIntegrationAssets();

			await Task.Run(() =>
			{
				_takeover.SetDefaultFileManager(enabled);
				ThrowIfIntegrationFailures();
			});
		}

		public bool IsOpenFileDialog()
			=> GetOpenFileDialogOwner() is ShellIntegrationOwner.Internal;

		public ShellIntegrationOwner GetOpenFileDialogOwner()
			=> _takeover.GetOpenFileDialogOwner();

		public async Task SetOpenFileDialogAsync(bool enabled)
		{
			if (enabled)
				CopyIntegrationAssets();

			await Task.Run(() =>
			{
				_takeover.SetOpenFileDialog(enabled);
				ThrowIfIntegrationFailures();
			});
		}

		private void ThrowIfIntegrationFailures()
		{
			if (_takeover.LastFailures.Count == 0)
				return;

			throw new ShellIntegrationOperationException(_takeover.LastFailures.ToArray());
		}

		private void CopyIntegrationAssets()
		{
			var sourceDirectory = Path.Combine(AppRuntime.Current.InstallDirectory, "Assets", "FilesOpenDialog");
			if (!Directory.Exists(sourceDirectory))
				throw new FileNotFoundException($"The shell integration assets were not found at '{sourceDirectory}'.");

			foreach (var fileName in RequiredIntegrationFileNames)
			{
				var sourcePath = Path.Combine(sourceDirectory, fileName);
				if (!File.Exists(sourcePath))
					throw new FileNotFoundException($"The shell integration asset '{sourcePath}' was not found.");
			}

			try
			{
				Directory.CreateDirectory(_integrationPaths.IntegrationDirectory);
				foreach (var fileName in RequiredIntegrationFileNames)
				{
					File.Copy(
						Path.Combine(sourceDirectory, fileName),
						Path.Combine(_integrationPaths.IntegrationDirectory, fileName),
						overwrite: true);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				App.Logger?.LogWarning(ex, "Unable to copy the shell integration files.");
				throw;
			}
		}

		internal static readonly string[] RequiredIntegrationFileNames =
		[
			"Files.App.Launcher.exe",
			"Files.App.OpenDialog32.dll",
			"Files.App.OpenDialog64.dll",
			"Files.App.SaveDialog32.dll",
			"Files.App.SaveDialog64.dll",
		];
	}
}
