// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Data.Contracts
{
	public interface IUpdateService : INotifyPropertyChanged
	{
		/// <summary>
		/// Gets a value indicating whether updates are available.
		/// </summary>
		bool IsUpdateAvailable { get; }

		/// <summary>
		/// Gets a value indicating if an update is in progress.
		/// </summary>
		bool IsUpdating { get; }

		/// <summary>
		/// Gets the percentage (0-100) of the update download/install in progress.
		/// </summary>
		int UpdateProgress { get; }

		/// <summary>
		/// Gets a value indicating if the app is being used the first time after an update.
		/// </summary>
		bool IsAppUpdated { get; }

		/// <summary>
		/// Gets a value that indicates if there are release notes available for the current version of the app.
		/// </summary>
		bool AreReleaseNotesAvailable { get; }

		/// <summary>
		/// Gets the version that is available for download, if any.
		/// </summary>
		Version? AvailableVersion { get; }

		/// <summary>
		/// Gets the release notes of the available version, if any.
		/// </summary>
		string? AvailableReleaseNotes { get; }

		/// <summary>
		/// Gets the page where the available version can be downloaded.
		/// </summary>
		Uri? ReleasePageUrl { get; }

		/// <summary>
		/// Gets the result of the last update check.
		/// </summary>
		UpdateCheckResult LastResult { get; }

		Task DownloadUpdatesAsync();

		Task DownloadMandatoryUpdatesAsync();

		Task CheckForUpdatesAsync(bool force = false);

		Task CheckForReleaseNotesAsync();

		/// <summary>
		/// Replace Files.App.Launcher.exe if it is used and has been updated
		/// </summary>
		Task CheckAndUpdateFilesLauncherAsync();
	}
}
