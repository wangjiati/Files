// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DateTimeFormatSettingsTests
	{
		private TemporaryDirectory _directory = null!;

		[TestInitialize]
		public void Initialize()
		{
			_directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, _directory.Path);
			Directory.CreateDirectory(AppRuntime.Current.SettingsDirectory);
		}

		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
			_directory.Dispose();
		}

		[TestMethod]
		public void MissingRulesReturnEmptyAndKeepExistingFormat()
		{
			File.WriteAllText(SettingsFilePath, "{\"schemaVersion\":1,\"DateTimeFormat\":2}");

			var generalSettings = LoadSettings();

			Assert.AreEqual(DateTimeFormats.Universal, generalSettings.DateTimeFormat);
			Assert.AreEqual(0, generalSettings.DateTimeColumnFormatRules.Count);
			Assert.AreEqual(0, generalSettings.DateTimeFormatDirectoryRules.Count);
			Assert.IsNull(generalSettings.DateTimeFormatCustomPattern);
		}

		[TestMethod]
		public void NewRulesRoundTripWithoutChangingGlobalDefault()
		{
			var generalSettings = LoadSettings();
			var columnRules = new List<DateTimeFormatRule>
			{
				new(DateTimeColumnKind.Modified, null, false, DateTimeFormats.Custom, "HH:mm:ss.fff"),
				new(DateTimeColumnKind.Deleted, null, false, DateTimeFormats.System, null),
			};
			var directoryRules = new List<DateTimeFormatRule>
			{
				new(null, @"D:\Docs", true, DateTimeFormats.Universal, null),
				new(DateTimeColumnKind.Created, @"D:\Docs", true, DateTimeFormats.Custom, "yyyy-MM-dd")
				{
					VolumeId = "1A2B3C4D",
					RelativePath = @"\Docs",
				},
			};

			generalSettings.DateTimeFormatCustomPattern = "yyyy-MM-dd HH:mm";
			generalSettings.DateTimeColumnFormatRules = columnRules;
			generalSettings.DateTimeFormatDirectoryRules = directoryRules;

			var reloaded = LoadSettings();

			Assert.AreEqual(DateTimeFormats.Application, reloaded.DateTimeFormat);
			Assert.AreEqual("yyyy-MM-dd HH:mm", reloaded.DateTimeFormatCustomPattern);
			CollectionAssert.AreEqual(columnRules, reloaded.DateTimeColumnFormatRules);
			CollectionAssert.AreEqual(directoryRules, reloaded.DateTimeFormatDirectoryRules);
			Assert.AreEqual("1A2B3C4D", reloaded.DateTimeFormatDirectoryRules[1].VolumeId);
			Assert.AreEqual(@"\Docs", reloaded.DateTimeFormatDirectoryRules[1].RelativePath);
		}

		[TestMethod]
		public void DirectoryRulesPersistVolumeIdentityForRemovableVolume()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "AB12CD34", @"\Projects");
			var writer = new GeneralSettingsService(new UserSettingsService().GetSharingContext(), volumes);

			writer.DateTimeFormatDirectoryRules =
			[
				new DateTimeFormatRule(null, @"E:/Projects/", true, DateTimeFormats.Universal, null),
			];

			var reloaded = LoadSettings();

			Assert.AreEqual(1, reloaded.DateTimeFormatDirectoryRules.Count);
			Assert.AreEqual(@"E:\Projects", reloaded.DateTimeFormatDirectoryRules[0].DirectoryPath);
			Assert.AreEqual("AB12CD34", reloaded.DateTimeFormatDirectoryRules[0].VolumeId);
			Assert.AreEqual(@"\Projects", reloaded.DateTimeFormatDirectoryRules[0].RelativePath);
		}

		[TestMethod]
		public void DuplicateScopesArePersistedOnce()
		{
			var writer = LoadSettings();

			writer.DateTimeColumnFormatRules =
			[
				new DateTimeFormatRule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.System, null),
				new DateTimeFormatRule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.Universal, null),
			];
			writer.DateTimeFormatDirectoryRules =
			[
				new DateTimeFormatRule(null, @"D:\Docs", false, DateTimeFormats.Universal, null),
				new DateTimeFormatRule(DateTimeColumnKind.Modified, @"D:\Docs", false, DateTimeFormats.System, null),
				new DateTimeFormatRule(DateTimeColumnKind.Modified, @"d:\docs\", false, DateTimeFormats.Custom, "yyyy"),
			];

			var reloaded = LoadSettings();

			Assert.AreEqual(1, reloaded.DateTimeColumnFormatRules.Count);
			Assert.AreEqual(DateTimeFormats.Universal, reloaded.DateTimeColumnFormatRules[0].Format);
			Assert.AreEqual(2, reloaded.DateTimeFormatDirectoryRules.Count);
			Assert.AreEqual(DateTimeFormats.Custom, reloaded.DateTimeFormatDirectoryRules[1].Format);
		}

		private string SettingsFilePath
			=> Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.UserSettingsFileName);

		private static IGeneralSettingsService LoadSettings()
		{
			var userSettings = new UserSettingsService();

			return new GeneralSettingsService(userSettings.GetSharingContext());
		}

		private sealed class FakeVolumePathProvider : IVolumePathProvider
		{
			private readonly Dictionary<string, (string VolumeId, string RelativePath)> _volumes = new(StringComparer.OrdinalIgnoreCase);

			public void AddVolume(string path, string volumeId, string relativePath)
				=> _volumes[PortableDirectoryKey.NormalizePath(path)] = (volumeId, relativePath);

			public bool TryGetVolumeIdentity(string directoryPath, out string volumeId, out string relativePath)
			{
				if (_volumes.TryGetValue(PortableDirectoryKey.NormalizePath(directoryPath), out var volume))
				{
					volumeId = volume.VolumeId;
					relativePath = volume.RelativePath;
					return true;
				}

				volumeId = string.Empty;
				relativePath = string.Empty;
				return false;
			}
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
