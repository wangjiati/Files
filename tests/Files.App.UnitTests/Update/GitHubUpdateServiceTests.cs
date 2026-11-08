// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests.Update
{
	[TestClass]
	public sealed class GitHubUpdateServiceTests
	{
		private const string NewerJson = """
		{
		  "tag_name": "v4.3.1.7",
		  "html_url": "https://github.com/wangjiati/Files/releases/tag/v4.3.1.7",
		  "body": "## 本版变化\n- 测试"
		}
		""";

		[TestMethod]
		public async Task CheckForUpdatesAsync_Disabled_DoesNotRequest()
		{
			var settings = new FakeSettings { CheckForUpdates = false };
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
			var service = CreateService(settings, new FakeLocalState(), handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(0, handler.Requests);
			Assert.AreEqual(UpdateCheckResult.Disabled, service.LastResult);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_HttpFailure_SetsFailedAndRecordsAttempt()
		{
			var handler = new StubHandler(HttpStatusCode.Forbidden, string.Empty);
			var state = new FakeLocalState();
			var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
			Assert.IsFalse(service.IsUpdateAvailable);
			Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), state.GetString(GitHubUpdateService.LastUpdateCheckDateKey));
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_NewerRelease_SetsUpdateAvailable()
		{
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
			var service = CreateService(new FakeSettings(), new FakeLocalState(), handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.UpdateAvailable, service.LastResult);
			Assert.AreEqual(new Version(4, 3, 1, 7), service.AvailableVersion);
			Assert.IsTrue(service.IsUpdateAvailable);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_SameVersion_SetsUpToDate()
		{
			var service = CreateService(new FakeSettings(), new FakeLocalState(), new StubHandler(HttpStatusCode.OK, NewerJson), new Version(4, 3, 1, 7));
			await service.CheckForUpdatesAsync();
			Assert.AreEqual(UpdateCheckResult.UpToDate, service.LastResult);
			Assert.IsFalse(service.IsUpdateAvailable);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_SkippedVersion_SetsSkipped()
		{
			var settings = new FakeSettings { SkippedVersion = "4.3.1.7" };
			var service = CreateService(settings, new FakeLocalState(), new StubHandler(HttpStatusCode.OK, NewerJson), new Version(4, 3, 1, 0));
			await service.CheckForUpdatesAsync();
			Assert.AreEqual(UpdateCheckResult.Skipped, service.LastResult);
			Assert.IsTrue(service.IsUpdateAvailable);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_CheckedToday_SkipsRequest()
		{
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
			var state = new FakeLocalState();
			state.SetString(GitHubUpdateService.LastUpdateCheckDateKey, DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"));
			var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(0, handler.Requests);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_Force_BypassesDailyGate()
		{
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
			var state = new FakeLocalState();
			state.SetString(GitHubUpdateService.LastUpdateCheckDateKey, DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"));
			var service = CreateService(new FakeSettings(), state, handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync(force: true);

			Assert.AreEqual(1, handler.Requests);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_RequestThrows_SetsFailedAndRecordsAttempt()
		{
			var state = new FakeLocalState();
			var service = CreateService(
				new FakeSettings(),
				state,
				new ThrowingHandler(new HttpRequestException("offline")),
				new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
			Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), state.GetString(GitHubUpdateService.LastUpdateCheckDateKey));
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_TooManyRequests_SetsFailed()
		{
			var service = CreateService(
				new FakeSettings(),
				new FakeLocalState(),
				new StubHandler(HttpStatusCode.TooManyRequests, string.Empty),
				new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_ServerError_SetsFailed()
		{
			var service = CreateService(
				new FakeSettings(),
				new FakeLocalState(),
				new StubHandler(HttpStatusCode.InternalServerError, string.Empty),
				new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_InvalidJson_SetsFailed()
		{
			var service = CreateService(
				new FakeSettings(),
				new FakeLocalState(),
				new StubHandler(HttpStatusCode.OK, "{ not json"),
				new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.Failed, service.LastResult);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_NewerVersionAfterSkip_SetsUpdateAvailable()
		{
			var settings = new FakeSettings { SkippedVersion = "4.3.1.7" };
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson.Replace("v4.3.1.7", "v4.3.1.8"));
			var service = CreateService(settings, new FakeLocalState(), handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(UpdateCheckResult.UpdateAvailable, service.LastResult);
			Assert.AreEqual(new Version(4, 3, 1, 8), service.AvailableVersion);
		}

		[TestMethod]
		public async Task CheckForUpdatesAsync_LocalStateWriteThrows_StillRequests()
		{
			var handler = new StubHandler(HttpStatusCode.OK, NewerJson);
			var service = CreateService(new FakeSettings(), new ThrowingLocalState(), handler, new Version(4, 3, 1, 0));

			await service.CheckForUpdatesAsync();

			Assert.AreEqual(1, handler.Requests);
			Assert.AreEqual(UpdateCheckResult.UpdateAvailable, service.LastResult);
		}

		private static GitHubUpdateService CreateService(FakeSettings settings, ILocalStateStore state, HttpMessageHandler handler, Version currentVersion)
			=> new(settings, state, new HttpClient(handler), () => currentVersion);

		private sealed class FakeSettings : IApplicationSettingsService
		{
			public bool HasClickedReviewPrompt { get; set; }
			public bool ShowRunningAsAdminPrompt { get; set; }
			public bool ShowDataStreamsAreHiddenPrompt { get; set; }
			public bool CheckForUpdates { get; set; } = true;
			public string? SkippedVersion { get; set; }
		}

		private sealed class FakeLocalState : ILocalStateStore
		{
			private readonly Dictionary<string, object?> _values = new();

			public bool GetBoolean(string key, bool defaultValue = false)
				=> _values.TryGetValue(key, out var value) && value is bool boolean ? boolean : defaultValue;

			public void SetBoolean(string key, bool value)
				=> _values[key] = value;

			public int GetInt32(string key, int defaultValue = 0)
				=> _values.TryGetValue(key, out var value) && value is int integer ? integer : defaultValue;

			public void SetInt32(string key, int value)
				=> _values[key] = value;

			public double GetDouble(string key, double defaultValue = 0d)
				=> _values.TryGetValue(key, out var value) && value is double number ? number : defaultValue;

			public void SetDouble(string key, double value)
				=> _values[key] = value;

			public string? GetString(string key, string? defaultValue = null)
				=> _values.TryGetValue(key, out var value) && value is string text ? text : defaultValue;

			public void SetString(string key, string value)
				=> _values[key] = value;

			public void Remove(string key)
				=> _values.Remove(key);
		}

		private sealed class StubHandler : HttpMessageHandler
		{
			private readonly HttpStatusCode _statusCode;
			private readonly string _body;

			public StubHandler(HttpStatusCode statusCode, string body)
			{
				_statusCode = statusCode;
				_body = body;
			}

			public int Requests { get; private set; }

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests++;
				return Task.FromResult(new HttpResponseMessage(_statusCode)
				{
					Content = new StringContent(_body),
				});
			}
		}

		private sealed class ThrowingHandler : HttpMessageHandler
		{
			private readonly Exception _exception;

			public ThrowingHandler(Exception exception)
			{
				_exception = exception;
			}

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
				=> Task.FromException<HttpResponseMessage>(_exception);
		}

		private sealed class ThrowingLocalState : ILocalStateStore
		{
			public bool GetBoolean(string key, bool defaultValue = false) => defaultValue;

			public void SetBoolean(string key, bool value) => throw new IOException("read-only");

			public int GetInt32(string key, int defaultValue = 0) => defaultValue;

			public void SetInt32(string key, int value) => throw new IOException("read-only");

			public double GetDouble(string key, double defaultValue = 0d) => defaultValue;

			public void SetDouble(string key, double value) => throw new IOException("read-only");

			public string? GetString(string key, string? defaultValue = null) => defaultValue;

			public void SetString(string key, string value) => throw new IOException("read-only");

			public void Remove(string key) => throw new IOException("read-only");
		}
	}
}
