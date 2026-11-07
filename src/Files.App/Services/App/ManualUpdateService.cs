// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Services
{
	internal sealed partial class ManualUpdateService : ObservableObject, IUpdateService
	{
		public bool IsUpdateAvailable => false;
		public bool IsUpdating => false;
		public int UpdateProgress => 0;
		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;
		public bool AreReleaseNotesAvailable => false;
		public Version? AvailableVersion => null;
		public string? AvailableReleaseNotes => null;
		public Uri? ReleasePageUrl => null;
		public UpdateCheckResult LastResult => UpdateCheckResult.NotChecked;

		public Task DownloadUpdatesAsync() => Task.CompletedTask;
		public Task DownloadMandatoryUpdatesAsync() => Task.CompletedTask;
		public Task CheckForUpdatesAsync(bool force = false) => Task.CompletedTask;
		public Task CheckForReleaseNotesAsync() => Task.CompletedTask;
		public Task CheckAndUpdateFilesLauncherAsync() => Task.CompletedTask;
	}
}
