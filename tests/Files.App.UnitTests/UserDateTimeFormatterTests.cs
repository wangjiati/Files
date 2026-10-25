// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Data.EventArguments;
using Files.App.Services.DateTimeFormatter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class UserDateTimeFormatterTests
	{
		private static readonly DateTimeOffset Sample =
			new(new DateTime(2024, 12, 31, 14, 30, 5, 123, DateTimeKind.Local));

		private TemporaryDirectory _directory = null!;

		[TestInitialize]
		public void Initialize()
		{
			_directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, _directory.Path);
		}

		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
			_directory.Dispose();
		}

		[TestMethod]
		public void CustomPattern_IsAppliedAndRefreshesWhenPatternChanges()
		{
			var source = new FakeSettingsSource
			{
				DateTimeFormat = DateTimeFormats.Custom,
				DateTimeFormatCustomPattern = "yyyy-MM-dd HH:mm:ss.fff",
			};
			var formatter = new UserDateTimeFormatter(source, new DateTimeFormatterFactory());

			Assert.AreEqual("2024-12-31 14:30:05.123", formatter.ToShortLabel(Sample));
			Assert.AreEqual("2024-12-31 14:30:05.123", formatter.ToLongLabel(Sample));

			source.DateTimeFormatCustomPattern = "yyyy/MM/dd";
			source.Raise(nameof(IGeneralSettingsService.DateTimeFormatCustomPattern));

			Assert.AreEqual("2024/12/31", formatter.ToShortLabel(Sample));
		}

		[TestMethod]
		public void CustomPattern_RefreshesWhenFormatSettingChanges()
		{
			var source = new FakeSettingsSource
			{
				DateTimeFormat = DateTimeFormats.Application,
				DateTimeFormatCustomPattern = "yyyy-MM-dd HH:mm:ss",
			};
			var formatter = new UserDateTimeFormatter(source, new DateTimeFormatterFactory());

			source.DateTimeFormat = DateTimeFormats.Custom;
			source.Raise(nameof(IGeneralSettingsService.DateTimeFormat));

			Assert.AreEqual("2024-12-31 14:30:05", formatter.ToShortLabel(Sample));
		}

		private sealed class FakeSettingsSource : IDateTimeFormatSettingsSource
		{
			public DateTimeFormats DateTimeFormat { get; set; }

			public string? DateTimeFormatCustomPattern { get; set; }

			public event EventHandler<SettingChangedEventArgs>? OnSettingChangedEvent;

			public void Raise(string settingName)
				=> OnSettingChangedEvent?.Invoke(this, new SettingChangedEventArgs(settingName, null));
		}

		private sealed class TemporaryDirectory : IDisposable
		{
			public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Files.Tests.{Guid.NewGuid():N}");

			public TemporaryDirectory()
			{
				Directory.CreateDirectory(Path);
			}

			public void Dispose()
			{
				Directory.Delete(Path, true);
			}
		}
	}
}
