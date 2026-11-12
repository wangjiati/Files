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
using System.Threading.Tasks;

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

			// The trailing debounce merges the burst into a few timer generations; the
			// essential invariant is that a burst of 100 changes never persists once per
			// change (a loaded host may split it into more than a single write).
			Thread.Sleep(SettingsFlushDebouncer.DebounceInterval + TimeSpan.FromMilliseconds(700));
			Assert.IsTrue(
				serializer.WriteCount is >= 1 and < 100,
				$"Expected the burst to merge into far fewer writes than one per change, got {serializer.WriteCount}");

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

		[TestCleanup]
		public void Cleanup()
		{
			// Each test creates app-lifetime singleton databases; without this their
			// debouncers (and timers) leak across tests and FlushAll would keep flushing
			// stale instances from previous tests. Production never calls this.
			SettingsFlushDebouncer.ResetForTests();
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

		[TestMethod]
		public void Settings_ConcurrentWriteDuringImportSurvives()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new BlockingImportSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			// Block the import inside its store write, which runs under the pending gate.
			serializer.BlockNextWrite();
			var import = Task.Run(() => database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));
			Assert.IsTrue(serializer.WriteEntered.WaitOne(TimeSpan.FromSeconds(10)), "The import did not reach its store write");

			// A SetValue arriving between the import's flush and its cache replacement must
			// wait for the gate and land on the imported cache, not be discarded by it.
			var write = Task.Run(() => Assert.IsTrue(database.SetValue("Racing", "post-import")));
			Thread.Sleep(200);

			serializer.Release();
			Assert.IsTrue(import.Wait(TimeSpan.FromSeconds(10)));
			Assert.IsTrue(import.Result, "The import must succeed");
			Assert.IsTrue(write.Wait(TimeSpan.FromSeconds(10)));

			Assert.AreEqual("yes", database.GetValue("Imported", "no"));
			Assert.AreEqual("post-import", database.GetValue("Racing", "lost"), "A write racing the import must not be lost");

			Assert.IsTrue(database.FlushSettings());
			var onDisk = JsonNode.Parse(serializer.ReadFromFile())!;
			Assert.AreEqual("yes", (string?)onDisk["Imported"]);
			Assert.AreEqual("post-import", (string?)onDisk["Racing"], "The racing write must reach disk after the import");
		}

		[TestMethod]
		public void Settings_WriteFlushedBeforeImportIsReplacedByImport()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new CountingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			// A write that is already pending when the import starts is pre-import state:
			// the import's pre-flush persists it, then the whole-store replacement drops it.
			Assert.IsTrue(database.SetValue("Old", "1"));
			Assert.IsTrue(database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));

			Assert.AreEqual("gone", database.GetValue("Old", "gone"));
			var onDisk = JsonNode.Parse(serializer.ReadFromFile())!;
			Assert.IsNull(onDisk["Old"]);
			Assert.AreEqual("yes", (string?)onDisk["Imported"]);
		}

		[TestMethod]
		public void Settings_ExportWithFailingFlushExportsDiskStateAndKeepsPending()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new ThrowingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			Assert.IsTrue(database.SetValue("Key", "pending"));

			// The pre-export flush fails: the export reflects the persisted state instead
			// of silently claiming the pending value, and the pending value is not lost.
			Assert.IsFalse(database.ExportSettings().ContainsKey("Key"));
			Assert.AreEqual("pending", database.GetValue("Key", "none"));

			serializer.ThrowOnWrite = false;
			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual("pending", (string?)JsonNode.Parse(serializer.ReadFromFile())!["Key"]);
		}

		[TestMethod]
		public void Settings_ImportAbortsWhenPreflushFails()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "user_settings.json");
			var serializer = new ThrowingSettingsSerializer();
			Assert.IsTrue(serializer.CreateFile(path));
			var database = new CachingJsonSettingsDatabase(
				serializer,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default);

			Assert.IsTrue(database.SetValue("Key", "pending"));

			// Replacing the store while pending writes cannot be persisted would discard
			// them, so the import must abort and leave the pending writes intact.
			Assert.IsFalse(database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));
			Assert.AreEqual("pending", database.GetValue("Key", "none"));
			Assert.AreEqual("none", database.GetValue("Imported", "none"));

			// Once writes persist again, the same import succeeds and replaces the store.
			serializer.ThrowOnWrite = false;
			Assert.IsTrue(database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));
			Assert.AreEqual("yes", database.GetValue("Imported", "no"));
			Assert.AreEqual("gone", database.GetValue("Key", "gone"));
		}

		private sealed class BlockingImportSerializer : ISettingsSerializer
		{
			private readonly DefaultSettingsSerializer inner = new();
			private readonly ManualResetEventSlim writeEntered = new(false);
			private readonly ManualResetEventSlim release = new(false);
			private bool blockNextWrite;

			public string? FilePath => inner.FilePath;

			public void BlockNextWrite() => blockNextWrite = true;

			public void Release() => release.Set();

			public WaitHandle WriteEntered => writeEntered.WaitHandle;

			public bool CreateFile(string path) => inner.CreateFile(path);

			public string ReadFromFile() => inner.ReadFromFile();

			public bool WriteToFile(string text)
			{
				var shouldBlock = blockNextWrite;
				blockNextWrite = false;

				if (shouldBlock)
				{
					writeEntered.Set();
					Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(30)), "The blocked import write was never released");
				}

				return inner.WriteToFile(text);
			}
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
