
using Files.App.Data.Enums;
using System.Net.Http;

namespace Files.App.Services
{
	internal sealed partial class DummyUpdateService : ObservableObject, IUpdateService
	{
		public bool IsUpdateAvailable => false;

		public bool IsUpdating => false;

		public int UpdateProgress => 0;

		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;

		private bool _areReleaseNotesAvailable = false;
		public bool AreReleaseNotesAvailable
		{
			get => _areReleaseNotesAvailable;
			private set => SetProperty(ref _areReleaseNotesAvailable, value);
		}

		public new event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

		public Version? AvailableVersion => null;

		public string? AvailableReleaseNotes => null;

		public Uri? ReleasePageUrl => null;

		public UpdateCheckResult LastResult => UpdateCheckResult.NotChecked;

		public Task CheckAndUpdateFilesLauncherAsync()
		{
			return Task.CompletedTask;
		}

		public Task CheckForUpdatesAsync(bool force = false)
		{
			return Task.CompletedTask;
		}

		public async Task CheckForReleaseNotesAsync()
		{
			using var client = new HttpClient();

			try
			{
				var response = await client.GetAsync(Constants.ExternalUrl.ReleaseNotesUrl);
				AreReleaseNotesAvailable = response.IsSuccessStatusCode;
			}
			catch
			{
				AreReleaseNotesAvailable = false;
			}
		}

		public Task DownloadMandatoryUpdatesAsync()
		{
			return Task.CompletedTask;
		}

		public Task DownloadUpdatesAsync()
		{
			return Task.CompletedTask;
		}
	}
}
