// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Services.Update;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http;
using Windows.System;

namespace Files.App.Services;

internal sealed partial class GitHubUpdateService : ObservableObject, IUpdateService
{
	internal const string LastUpdateCheckDateKey = "LastUpdateCheckDate";

	private const string ReleaseApiUrl = "https://api.github.com/repos/wangjiati/Files/releases/latest";

	private static readonly HttpClient SharedHttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(5),
	};

	private readonly IApplicationSettingsService _applicationSettings;
	private readonly ILocalStateStore _localState;
	private readonly HttpClient _httpClient;
	private readonly Func<Version> _currentVersion;

	public GitHubUpdateService(IApplicationSettingsService applicationSettings, ILocalStateStore localState)
		: this(applicationSettings, localState, SharedHttpClient, static () => AppRuntime.Current.Version)
	{
	}

	internal GitHubUpdateService(
		IApplicationSettingsService applicationSettings,
		ILocalStateStore localState,
		HttpClient httpClient,
		Func<Version> currentVersion)
	{
		_applicationSettings = applicationSettings;
		_localState = localState;
		_httpClient = httpClient;
		_currentVersion = currentVersion;
	}

	private bool _isUpdateAvailable;
	public bool IsUpdateAvailable
	{
		get => _isUpdateAvailable;
		private set => SetProperty(ref _isUpdateAvailable, value);
	}

	public bool IsUpdating => false;

	public int UpdateProgress => 0;

	public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;

	public bool AreReleaseNotesAvailable => false;

	private Version? _availableVersion;
	public Version? AvailableVersion
	{
		get => _availableVersion;
		private set => SetProperty(ref _availableVersion, value);
	}

	private string? _availableReleaseNotes;
	public string? AvailableReleaseNotes
	{
		get => _availableReleaseNotes;
		private set => SetProperty(ref _availableReleaseNotes, value);
	}

	private Uri? _releasePageUrl;
	public Uri? ReleasePageUrl
	{
		get => _releasePageUrl;
		private set => SetProperty(ref _releasePageUrl, value);
	}

	private UpdateCheckResult _lastResult;
	public UpdateCheckResult LastResult
	{
		get => _lastResult;
		private set => SetProperty(ref _lastResult, value);
	}

	public async Task CheckForUpdatesAsync(bool force = false)
	{
		if (!_applicationSettings.CheckForUpdates)
		{
			LastResult = UpdateCheckResult.Disabled;
			return;
		}

		var today = DateOnly.FromDateTime(DateTime.Now);
		if (!force && !UpdateCheckPolicy.ShouldCheckToday(_localState.GetString(LastUpdateCheckDateKey), today))
			return;

		try
		{
			_localState.SetString(LastUpdateCheckDateKey, today.ToString(UpdateCheckPolicy.DateFormat, CultureInfo.InvariantCulture));
		}
		catch (Exception ex)
		{
			// A read-only or locked portable data directory must not disable update checks.
			App.Logger?.LogWarning(ex, "Failed to record the update check date.");
		}

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl);
			request.Headers.UserAgent.ParseAdd($"Files-Internal/{_currentVersion()}");
			request.Headers.Accept.ParseAdd("application/vnd.github+json");

			using var response = await _httpClient.SendAsync(request);
			if (!response.IsSuccessStatusCode)
			{
				LastResult = UpdateCheckResult.Failed;
				return;
			}

			var json = await response.Content.ReadAsStringAsync();
			if (!GitHubReleaseParser.TryParseLatestRelease(json, out var release))
			{
				LastResult = UpdateCheckResult.Failed;
				return;
			}

			if (!UpdateVersionHelper.IsNewer(release.Version, _currentVersion()))
			{
				AvailableVersion = null;
				AvailableReleaseNotes = null;
				ReleasePageUrl = null;
				IsUpdateAvailable = false;
				LastResult = UpdateCheckResult.UpToDate;
				return;
			}

			AvailableVersion = release.Version;
			AvailableReleaseNotes = release.Notes;
			ReleasePageUrl = release.PageUrl;
			IsUpdateAvailable = true;

			LastResult = UpdateCheckPolicy.IsSkipped(release.Version, _applicationSettings.SkippedVersion)
				? UpdateCheckResult.Skipped
				: UpdateCheckResult.UpdateAvailable;
		}
		catch (Exception ex)
		{
			App.Logger?.LogWarning(ex, "Failed to check for updates.");
			LastResult = UpdateCheckResult.Failed;
		}
	}

	public Task DownloadUpdatesAsync()
	{
		if (ReleasePageUrl is { Scheme: "https" } uri &&
			string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
		{
			_ = Launcher.LaunchUriAsync(uri);
		}

		return Task.CompletedTask;
	}

	public Task DownloadMandatoryUpdatesAsync()
		=> Task.CompletedTask;

	public Task CheckForReleaseNotesAsync()
		=> Task.CompletedTask;

	public Task CheckAndUpdateFilesLauncherAsync()
		=> Task.CompletedTask;
}
