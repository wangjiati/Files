// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Microsoft.Win32;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Files.App.ViewModels.Settings
{
	/// <summary>
	/// Represents view model of <see cref="Views.Settings.AboutPage"/>.
	/// </summary>
	public sealed partial class AboutViewModel : ObservableObject
	{
		// Dependency injections

		private IGeneralSettingsService GeneralSettingsService { get; } = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
		private IApplicationSettingsService ApplicationSettingsService { get; } = Ioc.Default.GetRequiredService<IApplicationSettingsService>();
		private IUpdateService UpdateService { get; } = Ioc.Default.GetRequiredService<IUpdateService>();


		// Properties

		public string Version
			=> string.Format(
				Strings.SettingsAboutVersionFormat.GetLocalizedResource(),
				AppVersion.Major,
				AppVersion.Minor,
				AppVersion.Build,
				AppVersion.Revision);

		public string UpstreamVersion
			=> string.Format(
				Strings.SettingsAboutUpstreamFormat.GetLocalizedResource(),
				AppVersion.Major,
				AppVersion.Minor,
				AppVersion.Build);

		public string VersionWithUpstream => $"{Version}\n{UpstreamVersion}";

		public bool CheckForUpdates
		{
			get => ApplicationSettingsService.CheckForUpdates;
			set
			{
				if (ApplicationSettingsService.CheckForUpdates == value)
					return;

				ApplicationSettingsService.CheckForUpdates = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsCheckForUpdatesEnabled));
			}
		}

		public bool IsCheckForUpdatesEnabled => CheckForUpdates;

		public string UpdateStatus => UpdateService.LastResult switch
		{
			UpdateCheckResult.UpToDate
				=> Strings.UpdateStatusUpToDate.GetLocalizedResource(),
			UpdateCheckResult.UpdateAvailable when UpdateService.AvailableVersion is { } availableVersion
				=> string.Format(Strings.UpdateStatusAvailable.GetLocalizedResource(), availableVersion),
			UpdateCheckResult.Skipped when UpdateService.AvailableVersion is { } skippedVersion
				=> string.Format(Strings.UpdateStatusSkipped.GetLocalizedResource(), skippedVersion),
			UpdateCheckResult.Failed
				=> Strings.UpdateStatusFailed.GetLocalizedResource(),
			UpdateCheckResult.Disabled
				=> Strings.UpdateStatusDisabled.GetLocalizedResource(),
			_ => string.Empty,
		};

		public string AppName
			=> AppRuntime.Current.DisplayName;

		public Version AppVersion
			=> AppRuntime.Current.Version;

		public ObservableCollection<OpenSourceLibraryItem> OpenSourceLibraries { get; }

		// Commands

		public ICommand CopyAppVersionCommand { get; }
		public ICommand CopyWindowsVersionCommand { get; }
		public ICommand CopyUserIDCommand { get; }
		public ICommand OpenLogLocationCommand { get; }
		public ICommand OpenGitHubRepoCommand { get; }
		public ICommand OpenHomepageCommand { get; }
		public ICommand OpenFeedbackCommand { get; }
		public ICommand CheckForUpdatesCommand { get; }

		// Constructor

		/// <summary>
		/// Initializes an instance of <see cref="AboutViewModel"/> class.
		/// </summary>
		public AboutViewModel()
		{
			OpenSourceLibraries =
			[
				new ("https://github.com/omar/ByteSize", "ByteSize"),
				new ("https://github.com/CommunityToolkit/dotnet", "CommunityToolkit.Mvvm"),
				new ("https://github.com/DiscUtils/DiscUtils", "DiscUtils.Udf"),
				new ("https://github.com/robinrodricks/FluentFTP", "FluentFTP"),
				new ("https://github.com/libgit2/libgit2sharp", "libgit2sharp"),
				new ("https://github.com/jeffijoe/messageformat.net", "MessageFormat"),
				new ("https://github.com/dotnet/efcore", "EF Core for SQLite"),
				new ("https://github.com/dotnet/runtime", "Microsoft.Extensions"),
				new ("https://github.com/files-community/SevenZipSharp", "SevenZipSharp"),
				new ("https://sourceforge.net/projects/sevenzip", "7zip"),
				new ("https://github.com/ericsink/SQLitePCL.raw", "PCL for SQLite"),
				new ("https://github.com/microsoft/WindowsAppSDK", "WindowsAppSDK"),
				new ("https://github.com/microsoft/microsoft-ui-xaml", "WinUI 3"),
				new ("https://github.com/microsoft/Win2D", "Win2D"),
				new ("https://github.com/CommunityToolkit/Windows", "Windows Community Toolkit"),
				new ("https://github.com/mono/taglib-sharp", "TagLibSharp"),
				new ("https://github.com/microsoft/CsWin32", "CsWin32"),
				new ("https://github.com/microsoft/CsWinRT", "CsWinRT"),
				new ("https://github.com/GihanSoft/NaturalStringComparer", "NaturalStringComparer"),
				new ("https://github.com/leeqwind/PESignAnalyzer", "PESignAnalyzer"),
			];

			CopyAppVersionCommand = new RelayCommand(CopyAppVersion);
			CopyWindowsVersionCommand = new RelayCommand(CopyWindowsVersion);
			CopyUserIDCommand = new RelayCommand(CopyUserID);
			OpenGitHubRepoCommand = new AsyncRelayCommand(DoOpenGitHubRepo);
			OpenLogLocationCommand = new AsyncRelayCommand(OpenLogLocation);
			CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesNowAsync);
			OpenHomepageCommand = new AsyncRelayCommand(DoOpenHomepage);
			OpenFeedbackCommand = new AsyncRelayCommand(DoOpenFeedback);
		}

		// Methods

		private async Task<bool> OpenLogLocation()
		{
			var logFolder = await StorageFolder.GetFolderFromPathAsync(AppRuntime.Current.LogDirectory);
			await Launcher.LaunchFolderAsync(logFolder).AsTask();

			// TODO: Move this to an application service
			// Detect if Files is set as the default file manager
			using var subkey = Registry.ClassesRoot.OpenSubKey(@"Folder\shell\open\command");
			var command = (string?)subkey?.GetValue(string.Empty);

			// Close the settings dialog if Files is the default file manager
			if (!string.IsNullOrEmpty(command) && command.Contains("Files.App.Launcher.exe"))
				UIHelpers.CloseAllDialogs();

			return true;
		}

		private async Task CheckForUpdatesNowAsync()
		{
			await UpdateService.CheckForUpdatesAsync(force: true);
			OnPropertyChanged(nameof(UpdateStatus));
		}

		public Task DoOpenHomepage()
		{
			return Launcher.LaunchUriAsync(new Uri(Constants.ExternalUrl.HomepageUrl)).AsTask();
		}

		public Task DoOpenFeedback()
		{
			return Launcher.LaunchUriAsync(new Uri(Constants.ExternalUrl.FeedbackUrl)).AsTask();
		}

		public Task DoOpenGitHubRepo()
		{
			return Launcher.LaunchUriAsync(new Uri(Constants.ExternalUrl.GitHubRepoUrl)).AsTask();
		}

		public void CopyAppVersion()
		{
			SafetyExtensions.IgnoreExceptions(() =>
			{
				DataPackage dataPackage = new DataPackage();
				dataPackage.RequestedOperation = DataPackageOperation.Copy;
				dataPackage.SetText(GetAppVersion());
				Clipboard.SetContent(dataPackage);
			});
		}

		public void CopyWindowsVersion()
		{
			SafetyExtensions.IgnoreExceptions(() =>
			{
				DataPackage dataPackage = new DataPackage();
				dataPackage.RequestedOperation = DataPackageOperation.Copy;
				dataPackage.SetText(GetWindowsVersion());
				Clipboard.SetContent(dataPackage);
			});
		}

		public void CopyUserID()
		{
			SafetyExtensions.IgnoreExceptions(() =>
			{
				DataPackage dataPackage = new DataPackage();
				dataPackage.RequestedOperation = DataPackageOperation.Copy;
				dataPackage.SetText(GetUserID());
				Clipboard.SetContent(dataPackage);
			});
		}

		public string GetAppVersion()
		{
			return $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build}.{AppVersion.Revision}";
		}

		public string GetWindowsVersion()
		{
			ulong v = ulong.Parse(Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion);
			return $"{(v >> 48) & 0xFFFF}.{(v >> 32) & 0xFFFF}.{(v >> 16) & 0xFFFF}.{v & 0xFFFF}";
		}

		public string GetUserID()
		{
			return GeneralSettingsService.UserId;
		}
	}
}
