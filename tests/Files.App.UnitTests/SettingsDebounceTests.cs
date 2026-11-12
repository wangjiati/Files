// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.Settings;
using Files.App.Utils.Serialization;
using Files.App.Utils.Serialization.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class SettingsDebounceTests
	{
		private sealed class CountingSettingsSerializer : ISettingsSerializer
		{
			private readonly DefaultSettingsSerializer inner = new();

			public int WriteCount { get; private set; }

			public string? FilePath => inner.FilePath;

			public bool CreateFile(string path) => inner.CreateFile(path);

			public string ReadFromFile() => inner.ReadFromFile();

			public bool WriteToFile(string text)
			{
				WriteCount++;
				return inner.WriteToFile(text);
			}
		}

		[TestMethod]
		public void Settings_BurstOfWritesIsDebouncedWithReadThrough()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new CountingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			// A burst of 100 writes to different keys must not write once per change.
			for (var i = 0; i < 100; i++)
				Assert.IsTrue(database.SetValue("Key" + i, i));

			// Pending values are visible before the debounce fires (read-through).
			Assert.AreEqual(42, database.GetValue("Key42", -1));

			// The trailing debounce merges the burst into at most two writes.
			Thread.Sleep(SettingsFlushDebouncer.DebounceInterval + TimeSpan.FromMilliseconds(700));
			Assert.IsTrue(serializer.WriteCount is >= 1 and <= 2, $"Expected at most 2 writes for the burst, got {serializer.WriteCount}");

			// RemoveKey shares the same debounce.
			Assert.IsTrue(database.RemoveKey("Key50"));

			// The exit-path hook flushes every live database; terminal values must be complete on disk.
			SettingsFlushDebouncer.FlushAll();
			var fresh = new CachingJsonSettingsDatabase(
				new SerializerFor(path),
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);
			Assert.AreEqual(99, fresh.GetValue("Key99", -1), "The terminal value must survive the exit flush");
			Assert.AreEqual(-1, fresh.GetValue("Key50", -1), "The removed key must stay removed after the exit flush");
		}

		[TestMethod]
		public void Settings_ExplicitBoundariesFlushPendingWrites()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new CountingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			// SetValue only queues the write...
			Assert.IsTrue(database.SetValue("KeyA", "pending"));

			// ...and FlushSettings is a synchronous boundary: data is durable once it returns.
			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual("pending", JsonNode.Parse(serializer.ReadFromFile())!["KeyA"]!.GetValue<string>());

			// ExportSettings flushes pending writes first.
			Assert.IsTrue(database.SetValue("KeyB", 7));
			Assert.AreEqual(7, database.ExportSettings()["KeyB"].GetInt32());
			Assert.AreEqual(7, JsonNode.Parse(serializer.ReadFromFile())!["KeyB"]!.GetValue<int>());

			// ImportSettings stays an immediate full-store write.
			Assert.IsTrue(database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));
			Assert.AreEqual("yes", database.GetValue("Imported", "no"));
			Assert.AreEqual("yes", JsonNode.Parse(serializer.ReadFromFile())!["Imported"]!.GetValue<string>());

			// One write per explicit boundary, none for the debounced SetValue calls.
			Assert.AreEqual(3, serializer.WriteCount);
		}

		[TestMethod]
		public void Settings_UnexpectedFlushFailureKeepsPendingWritesForRetry()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new ThrowingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			Assert.IsTrue(database.SetValue("Key", "value"));

			// A non-IO failure during the write must keep the value pending...
			Assert.IsFalse(database.FlushSettings());
			Assert.AreEqual(1, serializer.WriteAttempts);

			// ...so an explicit retry persists the terminal value.
			serializer.ThrowOnWrite = false;
			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual(2, serializer.WriteAttempts);
			Assert.AreEqual("value", JsonNode.Parse(serializer.ReadFromFile())!["Key"]!.GetValue<string>());
		}

		private sealed class ThrowingSettingsSerializer : ISettingsSerializer
		{
			private readonly DefaultSettingsSerializer inner = new();

			public bool ThrowOnWrite { get; set; } = true;

			public int WriteAttempts { get; private set; }

			public string? FilePath => inner.FilePath;

			public bool CreateFile(string path) => inner.CreateFile(path);

			public string ReadFromFile() => inner.ReadFromFile();

			public bool WriteToFile(string text)
			{
				WriteAttempts++;

				if (ThrowOnWrite)
					throw new InvalidOperationException("Simulated unexpected settings write failure.");

				return inner.WriteToFile(text);
			}
		}

		private sealed class SerializerFor : ISettingsSerializer
		{
			private readonly DefaultSettingsSerializer inner = new();

			public SerializerFor(string path) => Assert.IsTrue(inner.CreateFile(path));

			public string? FilePath => inner.FilePath;

			public bool CreateFile(string path) => inner.CreateFile(path);

			public string ReadFromFile() => inner.ReadFromFile();

			public bool WriteToFile(string text) => inner.WriteToFile(text);
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
