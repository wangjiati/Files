// Copyright (c) Files Community
// Licensed under the MIT License.

#if DISTRIBUTION_MSIX
using Files.App.Server;
using Files.App.Services.Settings;
using Files.App.Utils.Serialization;
using Files.App.Utils.Serialization.Implementation;
using Files.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	/// <summary>
	/// Covers the MSIX-mode settings database through the IServerSettingsClient seam; the
	/// fake client replaces the out-of-process WinRT server so no server activation runs.
	/// </summary>
	[TestClass]
	public sealed class SettingsServerDatabaseTests
	{
		private readonly List<ServerSettingsDatabase> databases = [];

		[TestCleanup]
		public void Cleanup()
		{
			// Detach each database's static App.ServerChanged subscription and dispose its
			// debouncer, then clear any debouncers left from databases created inline.
			foreach (var database in databases)
				database.DisposeForTests();

			SettingsFlushDebouncer.ResetForTests();
		}

		[TestMethod]
		public void ServerSettings_BurstToSameKeyIsDedupedPerKey()
		{
			var fileName = "test_burst.json";
			var client = new FakeServerSettingsClient();
			var database = NewDatabase(fileName, client);

			// 100 writes to one key must push a single per-key operation on flush.
			for (var i = 0; i < 100; i++)
				Assert.IsTrue(database.SetValue("Key", i));

			// Pending values are visible before the push (read-through).
			Assert.AreEqual(99, database.GetValue("Key", -1));

			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual(1, client.SetSettingCalls, "A burst to one key must dedup to a single push");
			Assert.AreEqual(99d, client.PeekDouble(fileName, "Key"));

			Assert.IsTrue(database.RemoveKey("Key"));
			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual(1, client.RemoveSettingCalls);
			Assert.IsNull(client.PeekDouble(fileName, "Key"));
		}

		[TestMethod]
		public void ServerSettings_NewerEditDuringPushStaysPending()
		{
			var fileName = "test_reentrant.json";
			var client = new FakeServerSettingsClient();
			var database = NewDatabase(fileName, client);

			Assert.IsTrue(database.SetValue("Key", "first"));

			// A newer edit for the same key arriving while its push is in flight must stay
			// pending and be pushed again, not be dropped as already sent.
			client.OnSetSetting = (_, _) =>
			{
				client.OnSetSetting = null;
				Assert.IsTrue(database.SetValue("Key", "second"));
			};

			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual(2, client.SetSettingCalls);
			Assert.AreEqual("second", database.GetValue("Key", "none"));
			Assert.AreEqual("second", client.PeekString(fileName, "Key"));
		}

		[TestMethod]
		public void ServerSettings_ServerChangeOverlaysPendingWrites()
		{
			var fileName = "test_overlay.json";
			var client = new FakeServerSettingsClient();
			client.SetValueDirect(fileName, "ServerKey", "server1");

			var database = NewDatabase(fileName, client);

			var changes = new List<KeyValuePair<string, JsonElement>>();
			var changed = new ManualResetEventSlim(false);
			database.Changed += (key, value) =>
			{
				lock (changes)
					changes.Add(new(key, value));
				changed.Set();
			};

			Assert.IsTrue(database.SetValue("LocalKey", "pending"));

			// An external server-side change must not revert a local pending write.
			client.SetValueDirect(fileName, "ServerKey", "server2");
			RaiseServerChanged(ServerChangeNames.Settings(fileName));

			Assert.IsTrue(changed.Wait(TimeSpan.FromSeconds(5)), "No change notification arrived");
			Assert.AreEqual("pending", database.GetValue("LocalKey", "none"), "Pending local writes must not be reverted by server changes");
			Assert.AreEqual("server2", database.GetValue("ServerKey", "none"));

			lock (changes)
			{
				Assert.AreEqual(1, changes.Count);
				Assert.AreEqual("ServerKey", changes[0].Key);
				Assert.AreEqual("server2", changes[0].Value.GetString());
			}
		}

		[TestMethod]
		public void ServerSettings_ConcurrentWriteDuringImportSurvives()
		{
			var fileName = "test_importrace.json";
			var client = new FakeServerSettingsClient();
			var database = NewDatabase(fileName, client);

			Assert.IsTrue(database.SetValue("Stable", "s"));

			var importEntered = new ManualResetEventSlim(false);
			var releaseImport = new ManualResetEventSlim(false);
			client.OnImportSettings = () =>
			{
				importEntered.Set();
				Assert.IsTrue(releaseImport.Wait(TimeSpan.FromSeconds(30)), "The blocked import was never released");
			};

			var import = Task.Run(() => database.ImportSettings(new Dictionary<string, object?> { ["Imported"] = "yes" }));
			Assert.IsTrue(importEntered.Wait(TimeSpan.FromSeconds(10)), "The import did not reach its server call");

			// A write racing the import's pre-flush and cache replacement is per-key pending;
			// the post-import flush must push it over the imported data so it survives. The
			// fake holds pushes until the whole-store replacement is done, so whichever
			// thread pushes (debounce timer or explicit flush), the racing write lands
			// strictly after the import — the interleave is deterministic, no timing margin.
			var write = Task.Run(() => Assert.IsTrue(database.SetValue("Racing", "post-import")));
			Thread.Sleep(200);

			releaseImport.Set();
			Assert.IsTrue(import.Wait(TimeSpan.FromSeconds(10)));
			Assert.IsTrue(import.Result, "The import must succeed");
			Assert.IsTrue(write.Wait(TimeSpan.FromSeconds(10)));

			Assert.AreEqual("yes", database.GetValue("Imported", "no"));
			Assert.AreEqual("post-import", database.GetValue("Racing", "lost"), "A write racing the import must not be lost");
			Assert.AreEqual("post-import", client.PeekString(fileName, "Racing"), "The racing write must be persisted after the import");
			Assert.IsNull(client.PeekString(fileName, "Stable"), "Pre-import writes are replaced by the whole-store import");
		}

		[TestMethod]
		public void ServerSettings_ExportFlushesPendingAndSurvivesFlushFailure()
		{
			var fileName = "test_export.json";
			var client = new FakeServerSettingsClient();
			var database = NewDatabase(fileName, client);

			Assert.IsTrue(database.SetValue("Key", "pending"));
			Assert.AreEqual("pending", database.ExportSettings()["Key"].GetString());
			Assert.AreEqual("pending", client.PeekString(fileName, "Key"));

			// When the push fails, the export degrades to the persisted state and records
			// it; the pending value stays pending and is not lost.
			client.ThrowOnSetSetting = true;
			Assert.IsTrue(database.SetValue("Key2", "pending2"));
			Assert.IsFalse(database.ExportSettings().ContainsKey("Key2"));
			Assert.AreEqual("pending2", database.GetValue("Key2", "none"));

			client.ThrowOnSetSetting = false;
			Assert.IsTrue(database.FlushSettings());
			Assert.AreEqual("pending2", client.PeekString(fileName, "Key2"));
		}

		private ServerSettingsDatabase NewDatabase(string fileName, IServerSettingsClient client)
		{
			var database = new ServerSettingsDatabase(
				fileName,
				new DefaultJsonSettingsSerializer(),
				UserSettingsJsonSerializationContext.Default,
				client);
			databases.Add(database);
			return database;
		}

		private static void RaiseServerChanged(string name)
		{
			var handler = typeof(App)
				.GetField("ServerChanged", BindingFlags.Static | BindingFlags.NonPublic)?
				.GetValue(null) as EventHandler<string>;
			Assert.IsNotNull(handler, "App.ServerChanged must be raisable in tests");
			handler.Invoke(null, name);
		}

		/// <summary>
		/// In-memory stand-in for the out-of-process server settings service, storing
		/// JsonElement values encoded through the same SettingsValueCodec as the wire.
		/// Pushes that arrive while an import is replacing the store are held until the
		/// replacement completes, so a racing write is always applied as a post-import
		/// edit — the ordering the database contract promises.
		/// </summary>
		private sealed class FakeServerSettingsClient : IServerSettingsClient
		{
			private readonly object gate = new();
			private readonly Dictionary<string, Dictionary<string, JsonElement>> stores = [];
			private readonly object importGate = new();
			private bool importActive;

			public int SetSettingCalls { get; private set; }

			public int RemoveSettingCalls { get; private set; }

			public bool ThrowOnSetSetting { get; set; }

			public Action? OnImportSettings { get; set; }

			public Action<string, string>? OnSetSetting { get; set; }

			public SettingsValue[] GetSettings(string fileName)
			{
				lock (gate)
					return SettingsValueCodec.EncodeObject(Store(fileName));
			}

			public bool SetSetting(string fileName, string key, SettingsValue[] value)
			{
				if (ThrowOnSetSetting)
					throw new InvalidOperationException("Simulated server push failure.");

				WaitUntilImportCompletes();
				OnSetSetting?.Invoke(fileName, key);
				lock (gate)
					Store(fileName)[key] = SettingsValueCodec.Decode(value);

				SetSettingCalls++;
				return true;
			}

			public bool RemoveSetting(string fileName, string key)
			{
				WaitUntilImportCompletes();
				lock (gate)
					Store(fileName).Remove(key);

				RemoveSettingCalls++;
				return true;
			}

			public bool FlushSettings(string fileName) => !ThrowOnSetSetting;

			public bool ImportSettings(string fileName, SettingsValue[] values)
			{
				lock (importGate)
					importActive = true;

				try
				{
					OnImportSettings?.Invoke();
					lock (gate)
						stores[fileName] = SettingsValueCodec.DecodeObject(values);
				}
				finally
				{
					lock (importGate)
					{
						importActive = false;
						Monitor.PulseAll(importGate);
					}
				}

				return true;
			}

			internal void SetValueDirect(string fileName, string key, string value)
			{
				lock (gate)
					Store(fileName)[key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
			}

			internal string? PeekString(string fileName, string key)
			{
				lock (gate)
					return Store(fileName).TryGetValue(key, out var value) ? value.GetString() : null;
			}

			internal double? PeekDouble(string fileName, string key)
			{
				lock (gate)
					return Store(fileName).TryGetValue(key, out var value) ? value.GetDouble() : null;
			}

			private void WaitUntilImportCompletes()
			{
				lock (importGate)
				{
					while (importActive)
						Assert.IsTrue(Monitor.Wait(importGate, TimeSpan.FromSeconds(30)), "A push blocked by an import was never released");
				}
			}

			private Dictionary<string, JsonElement> Store(string fileName)
			{
				if (!stores.TryGetValue(fileName, out var store))
					stores[fileName] = store = [];

				return store;
			}
		}
	}
}
#endif
