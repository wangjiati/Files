// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services
{
	internal sealed partial class ManualUpdateService : ObservableObject, IUpdateService
	{
		public bool IsUpdateAvailable => false;
		public bool IsUpdating => false;
		public int UpdateProgress => 0;
		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;
		public bool AreReleaseNotesAvailable => false;

		public Task DownloadUpdatesAsync() => Task.CompletedTask;
		public Task DownloadMandatoryUpdatesAsync() => Task.CompletedTask;
		public Task CheckForUpdatesAsync() => Task.CompletedTask;
		public Task CheckForReleaseNotesAsync() => Task.CompletedTask;
		public Task CheckAndUpdateFilesLauncherAsync() => Task.CompletedTask;
	}
}
