// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Helpers;
using Files.App.Utils.FileTags;
using Files.App.Utils.Serialization.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
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
		public void PortableTags_RejectsFutureSchema()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "filetags.db");
			File.WriteAllText(path, "{\"schemaVersion\":2,\"items\":[]}");

			Assert.ThrowsExactly<NotSupportedException>(() => new PortableFileTagsDatabase(path));
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
		public void PortableLayout_RejectsFutureSchema()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.db");
			File.WriteAllText(path, "{\"schemaVersion\":2,\"items\":[]}");

			Assert.ThrowsExactly<NotSupportedException>(() => new PortableLayoutPreferencesDatabase(path));
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
