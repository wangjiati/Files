// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using SevenZip;
using System.IO;
using System.Text;
using System.Windows.Input;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Win32.Storage.FileSystem;

namespace Files.App.ViewModels.Settings
{
	public sealed partial class AdvancedViewModel : ObservableObject
	{
		private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private ICommonDialogService CommonDialogService { get; } = Ioc.Default.GetRequiredService<ICommonDialogService>();
		private ISystemIntegrationService SystemIntegrationService { get; } = Ioc.Default.GetRequiredService<ISystemIntegrationService>();
		public ICommandManager Commands { get; } = Ioc.Default.GetRequiredService<ICommandManager>();

		private readonly IFileTagsSettingsService fileTagsSettingsService = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();

		public ICommand SetAsDefaultExplorerCommand { get; }
		public ICommand SetAsOpenFileDialogCommand { get; }
		public ICommand ExportSettingsCommand { get; }
		public ICommand ImportSettingsCommand { get; }
		public AsyncRelayCommand OpenFilesOnWindowsStartupCommand { get; }
		public ICommand ClearThumbnailCacheCommand { get; }


		public AdvancedViewModel()
		{
			IsSetAsDefaultFileManager = SystemIntegrationService.IsDefaultFileManager();
			IsSetAsOpenFileDialog = SystemIntegrationService.IsOpenFileDialog();

			SetAsDefaultExplorerCommand = new AsyncRelayCommand(SetAsDefaultExplorerAsync);
			SetAsOpenFileDialogCommand = new AsyncRelayCommand(SetAsOpenFileDialogAsync);
			ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync);
			ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync);
			OpenFilesOnWindowsStartupCommand = new AsyncRelayCommand(OpenFilesOnWindowsStartupAsync);
			ClearThumbnailCacheCommand = new AsyncRelayCommand(ClearThumbnailCacheAsync);

			_ = DetectOpenFilesAtStartupAsync();
			_ = UpdateCacheSizeAsync();
		}

		private async Task SetAsDefaultExplorerAsync()
		{
			await Task.Yield();
			if (IsSetAsDefaultFileManager == SystemIntegrationService.IsDefaultFileManager())
				return;

			await SystemIntegrationService.SetDefaultFileManagerAsync(IsSetAsDefaultFileManager);
			await DetectResult();
		}

		private Task DetectResult()
		{
			IsSetAsDefaultFileManager = SystemIntegrationService.IsDefaultFileManager();
			if (!IsSetAsDefaultFileManager)
			{
				IsSetAsOpenFileDialog = false;
				return SetAsOpenFileDialogAsync();
			}

			return Task.CompletedTask;
		}

		private async Task SetAsOpenFileDialogAsync()
		{
			await Task.Yield();
			if (IsSetAsOpenFileDialog == SystemIntegrationService.IsOpenFileDialog())
				return;

			await SystemIntegrationService.SetOpenFileDialogAsync(IsSetAsOpenFileDialog);
			IsSetAsOpenFileDialog = SystemIntegrationService.IsOpenFileDialog();
		}

		private async Task ImportSettingsAsync()
		{
			string[] extensions = [Strings.ZipFileCapitalized.GetLocalizedResource(), "*.zip"];
			bool result = CommonDialogService.Open_FileOpenDialog(MainWindow.Instance.WindowHandle, false, extensions, Environment.SpecialFolder.Desktop, out var filePath);
			if (!result)
				return;

			try
			{
				var file = await StorageHelpers.ToStorageItem<BaseStorageFile>(filePath);
				if (file is null)
					throw new IOException($"The selected settings file '{filePath}' could not be read.");

				if (await ZipStorageFolder.FromStorageFileAsync(file) is not ZipStorageFolder zipFolder)
					return;

				var settingsFolder = await StorageFolder.GetFolderFromPathAsync(AppRuntime.Current.SettingsDirectory);

				// Import user settings
				var userSettingsFile = await zipFolder.GetFileAsync(Constants.LocalSettings.UserSettingsFileName);
				string importSettings = await userSettingsFile.ReadTextAsync();
				UserSettingsService.ImportSettings(importSettings);

				// Import file tags list and DB
				var fileTagsList = await zipFolder.GetFileAsync(Constants.LocalSettings.FileTagSettingsFileName);
				string importTags = await fileTagsList.ReadTextAsync();
				fileTagsSettingsService.ImportSettings(importTags);
				var fileTagsDB = await zipFolder.GetFileAsync(Constants.LocalSettings.FileTagSettingsDatabaseFileName);
				string importTagsDB = await fileTagsDB.ReadTextAsync();
				var tagDbInstance = FileTagsHelper.GetDbInstance();
				tagDbInstance.Import(importTagsDB);

				// Import layout preferences and DB
				var layoutPrefsDB = await zipFolder.GetFileAsync(Constants.LocalSettings.UserSettingsDatabaseFileName);
				string importPrefsDB = await layoutPrefsDB.ReadTextAsync();
				var layoutDbInstance = LayoutPreferencesManager.GetDatabaseManagerInstance();
				layoutDbInstance.Import(importPrefsDB);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Error importing settings");
				UIHelpers.CloseAllDialogs();
				await DialogDisplayHelper.ShowDialogAsync(Strings.SettingsImportErrorTitle.GetLocalizedResource(), Strings.SettingsImportErrorDescription.GetLocalizedResource());
			}
		}

		private async Task ExportSettingsAsync()
		{
			string[] extensions = [Strings.ZipFileCapitalized.GetLocalizedResource(), "*.zip"];
			bool result = CommonDialogService.Open_FileSaveDialog(MainWindow.Instance.WindowHandle, false, extensions, Environment.SpecialFolder.Desktop, out var filePath);
			if (!result)
				return;

			if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				filePath += ".zip";

			try
			{
				var handle = Win32PInvoke.CreateFileFromAppW(
					filePath,
					(uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_READ | FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE),
					Win32PInvoke.FILE_SHARE_READ | Win32PInvoke.FILE_SHARE_WRITE,
					nint.Zero,
					Win32PInvoke.CREATE_NEW,
					0,
					nint.Zero);

				Win32PInvoke.CloseHandle(handle);

				var file = await StorageHelpers.ToStorageItem<BaseStorageFile>(filePath);
				if (file is null)
					throw new IOException($"The settings export file '{filePath}' could not be opened.");

				await ZipStorageFolder.InitArchive(file, OutArchiveFormat.Zip);

				if (await ZipStorageFolder.FromStorageFileAsync(file) is not ZipStorageFolder zipFolder)
					return;

				// Export user settings
				var exportSettings = UTF8Encoding.UTF8.GetBytes((string)UserSettingsService.ExportSettings());
				await zipFolder.CreateFileAsync(new MemoryStream(exportSettings), Constants.LocalSettings.UserSettingsFileName, CreationCollisionOption.ReplaceExisting);

				// Export file tags list and DB
				var exportTags = UTF8Encoding.UTF8.GetBytes((string)fileTagsSettingsService.ExportSettings());
				await zipFolder.CreateFileAsync(new MemoryStream(exportTags), Constants.LocalSettings.FileTagSettingsFileName, CreationCollisionOption.ReplaceExisting);
				var tagDbInstance = FileTagsHelper.GetDbInstance();
				byte[] exportTagsDB = UTF8Encoding.UTF8.GetBytes(tagDbInstance.Export());
				await zipFolder.CreateFileAsync(new MemoryStream(exportTagsDB), Constants.LocalSettings.FileTagSettingsDatabaseFileName, CreationCollisionOption.ReplaceExisting);

				// Export layout preferences DB
				var layoutDbInstance = LayoutPreferencesManager.GetDatabaseManagerInstance();
				byte[] exportPrefsDB = UTF8Encoding.UTF8.GetBytes(layoutDbInstance.Export());
				await zipFolder.CreateFileAsync(new MemoryStream(exportPrefsDB), Constants.LocalSettings.UserSettingsDatabaseFileName, CreationCollisionOption.ReplaceExisting);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Error exporting settings");
			}
		}

		private bool isSetAsDefaultFileManager;
		public bool IsSetAsDefaultFileManager
		{
			get => isSetAsDefaultFileManager;
			set => SetProperty(ref isSetAsDefaultFileManager, value);
		}

		private bool isSetAsOpenFileDialog;
		public bool IsSetAsOpenFileDialog
		{
			get => isSetAsOpenFileDialog;
			set => SetProperty(ref isSetAsOpenFileDialog, value);
		}

		public bool SupportsSystemIntegration
			=> AppRuntime.Current.SupportsSystemIntegration;

		public bool IsAppEnvironmentDev
		{
			get => AppLifecycleHelper.AppEnvironment is AppEnvironment.Dev;
		}

		public bool SupportsOpenDialogIntegration
			=> IsAppEnvironmentDev && SupportsSystemIntegration;

		private FileSavePicker InitializeWithWindow(FileSavePicker obj)
		{
			WinRT.Interop.InitializeWithWindow.Initialize(obj, MainWindow.Instance.WindowHandle);

			return obj;
		}

		private FileOpenPicker InitializeWithWindow(FileOpenPicker obj)
		{
			WinRT.Interop.InitializeWithWindow.Initialize(obj, MainWindow.Instance.WindowHandle);

			return obj;
		}

		private bool openOnWindowsStartup;
		public bool OpenOnWindowsStartup
		{
			get => openOnWindowsStartup;
			set => SetProperty(ref openOnWindowsStartup, value);
		}

		private bool canOpenOnWindowsStartup;
		public bool CanOpenOnWindowsStartup
		{
			get => canOpenOnWindowsStartup;
			set => SetProperty(ref canOpenOnWindowsStartup, value);
		}

		public bool CloseToBackground
		{
			get => SupportsSystemIntegration && UserSettingsService.GeneralSettingsService.CloseToBackground;
			set
			{
				if (SupportsSystemIntegration && value != UserSettingsService.GeneralSettingsService.CloseToBackground)
				{
					UserSettingsService.GeneralSettingsService.CloseToBackground = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ShowSystemTrayIcon
		{
			get => SupportsSystemIntegration && UserSettingsService.GeneralSettingsService.ShowSystemTrayIcon;
			set
			{
				if (SupportsSystemIntegration && value != UserSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
				{
					UserSettingsService.GeneralSettingsService.ShowSystemTrayIcon = value;

					OnPropertyChanged();
				}
			}
		}

		// TODO remove when feature is marked as stable
		public bool ShowFlattenOptions
		{
			get => UserSettingsService.GeneralSettingsService.ShowFlattenOptions;
			set
			{
				if (value == UserSettingsService.GeneralSettingsService.ShowFlattenOptions)
					return;

				UserSettingsService.GeneralSettingsService.ShowFlattenOptions = value;
				OnPropertyChanged();
			}
		}

		public bool EnableThumbnailCache
		{
			get => UserSettingsService.GeneralSettingsService.EnableThumbnailCache;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.EnableThumbnailCache)
				{
					UserSettingsService.GeneralSettingsService.EnableThumbnailCache = value;
					OnPropertyChanged();
				}
			}
		}

		public double ThumbnailCacheSizeLimit
		{
			get => UserSettingsService.GeneralSettingsService.ThumbnailCacheSizeLimit;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ThumbnailCacheSizeLimit)
				{
					UserSettingsService.GeneralSettingsService.ThumbnailCacheSizeLimit = value;
					OnPropertyChanged();
				}
			}
		}

		private string cacheSizeText = string.Empty;
		public string CacheSizeText
		{
			get => cacheSizeText;
			set => SetProperty(ref cacheSizeText, value);
		}

		private bool isClearCacheButtonEnabled;
		public bool IsClearCacheButtonEnabled
		{
			get => isClearCacheButtonEnabled;
			set => SetProperty(ref isClearCacheButtonEnabled, value);
		}

		private async Task ClearThumbnailCacheAsync()
		{
			//TODO: Clear thumbnail cache.
		}

		private async Task UpdateCacheSizeAsync()
		{
			//TODO: Get thumbnail cache size and update CacheSizeText and IsClearCacheButtonEnabled accordingly.
		}

		public async Task OpenFilesOnWindowsStartupAsync()
		{
			var stateMode = await ReadState();
			bool state = stateMode is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
			if (state != OpenOnWindowsStartup)
			{
				await SystemIntegrationService.SetStartupTaskEnabledAsync(OpenOnWindowsStartup);
				await DetectOpenFilesAtStartupAsync();
			}
		}

		public async Task DetectOpenFilesAtStartupAsync()
		{
			if (!AppRuntime.Current.SupportsSystemIntegration)
			{
				CanOpenOnWindowsStartup = false;
				OpenOnWindowsStartup = false;
				return;
			}

			var stateMode = await ReadState();

			switch (stateMode)
			{
				case StartupTaskState.Disabled:
					CanOpenOnWindowsStartup = true;
					OpenOnWindowsStartup = false;
					break;
				case StartupTaskState.Enabled:
					CanOpenOnWindowsStartup = true;
					OpenOnWindowsStartup = true;
					break;
				case StartupTaskState.DisabledByPolicy:
					CanOpenOnWindowsStartup = false;
					OpenOnWindowsStartup = false;
					break;
				case StartupTaskState.DisabledByUser:
					CanOpenOnWindowsStartup = false;
					OpenOnWindowsStartup = false;
					break;
				case StartupTaskState.EnabledByPolicy:
					CanOpenOnWindowsStartup = false;
					OpenOnWindowsStartup = true;
					break;
			}
		}

		public Task<StartupTaskState> ReadState()
			=> SystemIntegrationService.GetStartupTaskStateAsync();
	}
}
