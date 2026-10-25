// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Helpers;
using Files.App.Services;
using Files.App.Services.Settings;
using Files.App.Utils.FileTags;
using Files.App.Utils.Serialization;
using Files.App.Utils.Serialization.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DistributionDataTests
	{
		[TestMethod]
		public void Settings_WritesVersionedSchemaAndRoundTripsAfterMove()
		{
			using var directory = new TemporaryDirectory();
			var originalPath = Path.Combine(directory.Path, "first", "user_settings.json");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(originalPath));
			Assert.IsTrue(serializer.WriteToFile("{\"ShowFileTagsSection\":true}"));

			var root = JsonNode.Parse(File.ReadAllText(originalPath))!.AsObject();
			Assert.AreEqual(1, root["schemaVersion"]!.GetValue<int>());
			Assert.IsTrue(root["ShowFileTagsSection"]!.GetValue<bool>());
			Assert.IsNull(root["settings"]);

			var movedPath = Path.Combine(directory.Path, "moved 中文", "user_settings.json");
			Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
			File.Copy(originalPath, movedPath);
			var movedSerializer = new DefaultSettingsSerializer();
			Assert.IsTrue(movedSerializer.CreateFile(movedPath));
			Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"ShowFileTagsSection\":true}"), JsonNode.Parse(movedSerializer.ReadFromFile())));

			Assert.IsTrue(movedSerializer.WriteToFile("{\"ShowFileTagsSection\":false,\"AfterMove\":\"persisted\"}"));
			var reloadedSerializer = new DefaultSettingsSerializer();
			Assert.IsTrue(reloadedSerializer.CreateFile(movedPath));
			Assert.IsTrue(JsonNode.DeepEquals(
				JsonNode.Parse("{\"ShowFileTagsSection\":false,\"AfterMove\":\"persisted\"}"),
				JsonNode.Parse(reloadedSerializer.ReadFromFile())));
		}

		[TestMethod]
		public void Settings_ReadsNewEmptyFile()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));

			Assert.AreEqual(string.Empty, serializer.ReadFromFile());
		}

		[TestMethod]
		public void Settings_ReadsLegacyUnwrappedJson()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			File.WriteAllText(path, "{\"ShowFileTagsSection\":true}");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));

			Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"ShowFileTagsSection\":true}"), JsonNode.Parse(serializer.ReadFromFile())));
		}

		[TestMethod]
		public void Settings_RejectsFutureSchema()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			File.WriteAllText(path, "{\"schemaVersion\":2,\"ShowFileTagsSection\":true}");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));

			Assert.ThrowsExactly<NotSupportedException>(() => serializer.ReadFromFile());
		}

		[TestMethod]
		public void Settings_FutureSchemaFallsBackAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			const string futureSettings = "{\"schemaVersion\":2,\"ShowFileTagsSection\":true}";
			File.WriteAllText(path, futureSettings);
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			Assert.IsFalse(database.GetValue("ShowFileTagsSection", false));
			var backups = Directory.GetFiles(directory.Path, "user_settings.json.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual(futureSettings, File.ReadAllText(backups[0]));

			Assert.IsTrue(database.SetValue("ShowFileTagsSection", true));
			Assert.IsTrue(database.GetValue("ShowFileTagsSection", false));
		}

		[TestMethod]
		public void Settings_MalformedJsonFallsBackAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			File.WriteAllText(path, "{not-json");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			Assert.IsFalse(database.GetValue("ShowFileTagsSection", false));
			var backups = Directory.GetFiles(directory.Path, "user_settings.json.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual("{not-json", File.ReadAllText(backups[0]));
		}

		[TestMethod]
		public void Settings_FailedReplacementPreservesPreviousData()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new DefaultSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			Assert.IsTrue(serializer.WriteToFile("{\"value\":\"old\"}"));

			using (var destinationLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				Assert.IsFalse(serializer.WriteToFile("{\"value\":\"new\"}"));

			Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"value\":\"old\"}"), JsonNode.Parse(serializer.ReadFromFile())));
		}

		[TestMethod]
		public void PortableTags_RoundTripAfterMoveWithoutChangingExportSchema()
		{
			using var directory = new TemporaryDirectory();
			var originalPath = Path.Combine(directory.Path, "first", "filetags.db");
			var database = new PortableFileTagsDatabase(originalPath);
			database.SetTags(@"D:\Documents\report.pdf", 42, ["work", "home"]);

			var root = JsonNode.Parse(File.ReadAllText(originalPath))!.AsObject();
			Assert.AreEqual(1, root["schemaVersion"]!.GetValue<int>());
			Assert.AreEqual(1, root["items"]!.AsArray().Count);

			var movedPath = Path.Combine(directory.Path, "moved 中文", "filetags.db");
			Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
			File.Copy(originalPath, movedPath);
			var movedDatabase = new PortableFileTagsDatabase(movedPath);
			CollectionAssert.AreEqual(new[] { "work", "home" }, movedDatabase.GetTags(@"D:\Documents\report.pdf", null));
			CollectionAssert.AreEqual(new[] { "work", "home" }, movedDatabase.GetTags(null, 42));

			var exported = JsonNode.Parse(movedDatabase.Export())!.AsArray();
			Assert.AreEqual(1, exported.Count);
			Assert.AreEqual(@"D:\Documents\report.pdf", exported[0]!["FilePath"]!.GetValue<string>());
			Assert.AreEqual((ulong)42, exported[0]!["Frn"]!.GetValue<ulong>());
			Assert.IsNull(exported[0]!["schemaVersion"]);

			movedDatabase.SetTags(@"D:\Documents\report.pdf", 42, ["after-move"]);
			var reloadedDatabase = new PortableFileTagsDatabase(movedPath);
			CollectionAssert.AreEqual(new[] { "after-move" }, reloadedDatabase.GetTags(@"D:\Documents\report.pdf", null));
			CollectionAssert.AreEqual(new[] { "after-move" }, reloadedDatabase.GetTags(null, 42));
		}

		[TestMethod]
		public void PortableTags_ImportExportRoundTripsExistingArrayFormat()
		{
			using var directory = new TemporaryDirectory();
			var database = new PortableFileTagsDatabase(Path.Combine(directory.Path, "filetags.db"));
			const string import = "[{\"Frn\":7,\"FilePath\":\"E:\\\\Docs\\\\a.txt\",\"Tags\":[\"important\"]}]";

			database.Import(import);
			var exported = database.Export();

			Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(import), JsonNode.Parse(exported)));
			CollectionAssert.AreEqual(new[] { "important" }, database.GetTags(null, 7));
		}

		[TestMethod]
		public void PortableTags_GetTagsByRenameAndFrnReturnsTheUpdatedItem()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "filetags.db");
			var database = new PortableFileTagsDatabase(path);
			database.SetTags(@"D:\Documents\old.txt", 42, ["work"]);

			CollectionAssert.AreEqual(new[] { "work" }, database.GetTags(@"D:\Documents\new.txt", 42));
			CollectionAssert.AreEqual(new[] { "work" }, database.GetTags(@"D:\Documents\new.txt", null));
			CollectionAssert.AreEqual(new[] { "work" }, database.GetTags(null, 42));
			Assert.AreEqual(0, database.GetTags(@"D:\Documents\old.txt", null).Length);

			var reloaded = new PortableFileTagsDatabase(path);
			CollectionAssert.AreEqual(new[] { "work" }, reloaded.GetTags(@"D:\Documents\new.txt", null));
		}

		[TestMethod]
		public void PortableTags_FutureSchemaFallsBackAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "filetags.db");
			const string futureTags = "{\"schemaVersion\":2,\"items\":[]}";
			File.WriteAllText(path, futureTags);

			var database = new PortableFileTagsDatabase(path);

			Assert.AreEqual(0, database.GetAll().Count());
			var backups = Directory.GetFiles(directory.Path, "filetags.db.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual(futureTags, File.ReadAllText(backups[0]));
		}

		[TestMethod]
		public void PortableTags_FailedReplacementPreservesPreviousData()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "filetags.db");
			var database = new PortableFileTagsDatabase(path);
			database.SetTags(@"D:\old.txt", null, ["old"]);

			using (var destinationLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				Exception? failure = null;
				try
				{
					database.SetTags(@"D:\new.txt", null, ["new"]);
				}
				catch (Exception ex)
				{
					failure = ex;
				}

				Assert.IsTrue(failure is IOException or UnauthorizedAccessException, $"Unexpected replacement failure: {failure}");
			}

			var reloaded = new PortableFileTagsDatabase(path);
			CollectionAssert.AreEqual(new[] { "old" }, reloaded.GetTags(@"D:\old.txt", null));
			Assert.AreEqual(0, reloaded.GetTags(@"D:\new.txt", null).Length);
		}

		[TestMethod]
		public void IntegrationAssets_MissingSourceFailsExplicitly()
		{
			using var directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, directory.Path);
			var service = new WindowsSystemIntegrationService();

			Assert.ThrowsExactly<FileNotFoundException>(
				() => service.SetDefaultFileManagerAsync(true).GetAwaiter().GetResult());
		}

		[TestMethod]
		public void Portable_CloseToBackgroundIsIgnoredAtTheBehaviorLevel()
		{
			using var directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, directory.Path);
			Directory.CreateDirectory(AppRuntime.Current.SettingsDirectory);
			File.WriteAllText(
				Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.UserSettingsFileName),
				"{\"schemaVersion\":1,\"CloseToBackground\":true}");

			var userSettings = new UserSettingsService();
			var generalSettings = new GeneralSettingsService(userSettings.GetSharingContext());

			Assert.IsFalse(generalSettings.CloseToBackground);
			generalSettings.CloseToBackground = true;
			Assert.IsFalse(generalSettings.CloseToBackground);
		}

		[TestMethod]
		public void PortableLayout_UsesVersionedSchemaAndExistingImportExportFormat()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.db");
			var database = new PortableLayoutPreferencesDatabase(path);

			database.Import("[]");

			var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
			Assert.AreEqual(1, root["schemaVersion"]!.GetValue<int>());
			Assert.AreEqual(0, root["items"]!.AsArray().Count);
			Assert.AreEqual("[]", database.Export());
		}

		[TestMethod]
		public void PortableLayout_NonEmptyImportExportReloadAndLookupRoundTrips()
		{
			using var directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, directory.Path);
			Directory.CreateDirectory(AppRuntime.Current.SettingsDirectory);
			var userSettings = new UserSettingsService();
			Ioc.Default.ConfigureServices(new ServiceCollection()
				.AddSingleton<IUserSettingsService>(userSettings)
				.AddSingleton<ILayoutSettingsService>(new LayoutSettingsService(userSettings.GetSharingContext()))
				.BuildServiceProvider());
			var path = Path.Combine(directory.Path, "user_settings.db");
			const string originalFilePath = @"D:\Documents";
			const string movedFilePath = @"E:\Recovered";
			const ulong frn = 1234;
			var database = new PortableLayoutPreferencesDatabase(path);
			database.SetPreferences(originalFilePath, frn, CreateLayoutPreferences());

			var exported = database.Export();
			var exportedItems = JsonNode.Parse(exported)!.AsArray();
			Assert.AreEqual(1, exportedItems.Count);
			var exportedItem = exportedItems[0]!;
			Assert.AreEqual(originalFilePath, exportedItem["FilePath"]!.GetValue<string>());
			Assert.AreEqual(frn, exportedItem["Frn"]!.GetValue<ulong>());
			var manager = exportedItem["LayoutPreferencesManager"]!;
			Assert.IsTrue(manager["SortFilesFirst"]!.GetValue<bool>());
			Assert.AreEqual((int)FolderLayoutModes.DetailsView, manager["LayoutMode"]!.GetValue<int>());
			var nameColumn = manager["ColumnsViewModel"]!["NameColumn"]!;
			Assert.IsTrue(nameColumn["UserCollapsed"]!.GetValue<bool>());
			Assert.AreEqual(321.5d, nameColumn["UserLengthPixels"]!.GetValue<double>());
			Assert.AreEqual(321.5d, nameColumn["UserLength"]!["Value"]!.GetValue<double>());

			database.Import(exported);
			var reexported = database.Export();
			Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(exported), JsonNode.Parse(reexported)));

			var reloaded = new PortableLayoutPreferencesDatabase(path);
			AssertLayoutPreferences(reloaded.GetPreferences(originalFilePath, null));
			AssertLayoutPreferences(reloaded.GetPreferences(movedFilePath, frn));

			var reloadedAfterFrnLookup = new PortableLayoutPreferencesDatabase(path);
			AssertLayoutPreferences(reloadedAfterFrnLookup.GetPreferences(movedFilePath, null));
		}

		[TestMethod]
		public void PortableLayout_FutureSchemaFallsBackAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.db");
			const string futureLayout = "{\"schemaVersion\":2,\"items\":[]}";
			File.WriteAllText(path, futureLayout);

			var database = new PortableLayoutPreferencesDatabase(path);

			Assert.AreEqual("[]", database.Export());
			var backups = Directory.GetFiles(directory.Path, "user_settings.db.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual(futureLayout, File.ReadAllText(backups[0]));
		}

		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
		}

		private static LayoutPreferencesItem CreateLayoutPreferences()
		{
			var preferences = new LayoutPreferencesItem();
			preferences.SortDirectoriesAlongsideFiles = true;
			preferences.SortFilesFirst = true;
			preferences.IsAdaptiveLayoutOverridden = true;
			preferences.LayoutMode = FolderLayoutModes.DetailsView;
			preferences.DirectorySortOption = SortOption.Size;
			preferences.DirectorySortDirection = SortDirection.Descending;
			preferences.DirectoryGroupDirection = SortDirection.Ascending;
			preferences.DirectoryGroupOption = GroupOption.DateModified;
			preferences.DirectoryGroupByDateUnit = GroupByDateUnit.Month;
			preferences.ColumnsViewModel.NameColumn.UserCollapsed = true;
			preferences.ColumnsViewModel.NameColumn.UserLengthPixels = 321.5d;
			preferences.ColumnsViewModel.DateModifiedColumn.UserLengthPixels = 234.5d;
			return preferences;
		}

		private static void AssertLayoutPreferences(LayoutPreferencesItem? preferences)
		{
			Assert.IsNotNull(preferences);
			Assert.IsTrue(preferences.SortDirectoriesAlongsideFiles);
			Assert.IsTrue(preferences.SortFilesFirst);
			Assert.IsTrue(preferences.IsAdaptiveLayoutOverridden);
			Assert.AreEqual(FolderLayoutModes.DetailsView, preferences.LayoutMode);
			Assert.AreEqual(SortOption.Size, preferences.DirectorySortOption);
			Assert.AreEqual(SortDirection.Descending, preferences.DirectorySortDirection);
			Assert.AreEqual(SortDirection.Ascending, preferences.DirectoryGroupDirection);
			Assert.AreEqual(GroupOption.DateModified, preferences.DirectoryGroupOption);
			Assert.AreEqual(GroupByDateUnit.Month, preferences.DirectoryGroupByDateUnit);
			Assert.IsTrue(preferences.ColumnsViewModel.NameColumn.UserCollapsed);
			Assert.AreEqual(321.5d, preferences.ColumnsViewModel.NameColumn.UserLengthPixels);
			Assert.AreEqual(234.5d, preferences.ColumnsViewModel.DateModifiedColumn.UserLengthPixels);
		}

		private sealed partial class TemporaryDirectory : IDisposable
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
