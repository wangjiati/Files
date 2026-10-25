// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class LocalStateTests
	{
		[TestMethod]
		public void Json_RoundTripsSupportedValues()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			var store = new JsonLocalStateStore(path);

			store.SetBoolean("bool", true);
			store.SetInt32("int", 42);
			store.SetDouble("double", 12.5d);
			store.SetString("string", "Portable 中文");

			var reloaded = new JsonLocalStateStore(path);
			Assert.IsTrue(reloaded.GetBoolean("bool"));
			Assert.AreEqual(42, reloaded.GetInt32("int"));
			Assert.AreEqual(12.5d, reloaded.GetDouble("double"));
			Assert.AreEqual("Portable 中文", reloaded.GetString("string"));
			Assert.IsFalse(reloaded.GetBoolean("missing"));
			Assert.AreEqual(7, reloaded.GetInt32("missing-int", 7));
			Assert.AreEqual("fallback", reloaded.GetString("missing-string", "fallback"));
		}

		[TestMethod]
		public void Json_RemoveDeletesOnlyTheRequestedValue()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			var store = new JsonLocalStateStore(path);
			store.SetBoolean("remove", true);
			store.SetString("keep", "value");

			store.Remove("remove");

			var reloaded = new JsonLocalStateStore(path);
			Assert.IsFalse(reloaded.GetBoolean("remove", false));
			Assert.AreEqual("value", reloaded.GetString("keep"));
		}

		[TestMethod]
		public void Json_MalformedDataDegradesAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			File.WriteAllText(path, "{not-json");

			var store = new JsonLocalStateStore(path);

			Assert.IsFalse(store.GetBoolean("missing"));
			Assert.AreEqual("fallback", store.GetString("missing-string", "fallback"));
			var backups = Directory.GetFiles(directory.Path, "local-state.json.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual("{not-json", File.ReadAllText(backups[0]));
		}

		[TestMethod]
		public void Json_FutureSchemaDegradesAndPreservesTheOriginal()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			const string futureState = "{\"schemaVersion\":2,\"values\":{}}";
			File.WriteAllText(path, futureState);

			var store = new JsonLocalStateStore(path);

			Assert.IsFalse(store.GetBoolean("missing"));
			var backups = Directory.GetFiles(directory.Path, "local-state.json.corrupt-*");
			Assert.AreEqual(1, backups.Length);
			Assert.AreEqual(futureState, File.ReadAllText(backups[0]));
		}

		[TestMethod]
		public void Json_TypeMismatchFallsBackToDefaultsWithoutDroppingOtherValues()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			var store = new JsonLocalStateStore(path);
			store.SetString("typed", "text");
			store.SetString("keep", "value");

			Assert.IsFalse(store.GetBoolean("typed", false));
			Assert.AreEqual(7, store.GetInt32("typed", 7));
			store.SetString("after", "write");

			var reloaded = new JsonLocalStateStore(path);
			Assert.AreEqual("text", reloaded.GetString("typed"));
			Assert.AreEqual("value", reloaded.GetString("keep"));
			Assert.AreEqual("write", reloaded.GetString("after"));
		}

		[TestMethod]
		public void Json_FailedReplacementPreservesThePreviousFile()
		{
			using var directory = new TemporaryDirectory();
			var path = Path.Combine(directory.Path, "local-state.json");
			var store = new JsonLocalStateStore(path);
			store.SetString("key", "old");

			using (var destinationLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				Exception? failure = null;
				try
				{
					store.SetString("key", "new");
				}
				catch (Exception ex)
				{
					failure = ex;
				}

				Assert.IsTrue(failure is IOException or UnauthorizedAccessException, $"Unexpected replacement failure: {failure}");
			}

			var reloaded = new JsonLocalStateStore(path);
			Assert.AreEqual("old", reloaded.GetString("key"));
		}

		[TestMethod]
		public void PortableWriteProbe_CreatesDataDirectoriesAndRemovesTheProbe()
		{
			using var directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, directory.Path);
			try
			{
				using var lease = Program.PreparePortableData();
				var environment = AppRuntime.Current;

				Assert.IsTrue(Directory.Exists(environment.DataDirectory));
				Assert.IsTrue(Directory.Exists(environment.SettingsDirectory));
				Assert.IsTrue(Directory.Exists(environment.LogDirectory));
				Assert.IsTrue(Directory.Exists(environment.CacheDirectory));
				Assert.AreEqual(0, Directory.GetFiles(environment.DataDirectory, "*.write-probe").Length);
				Assert.IsTrue(File.Exists(Path.Combine(environment.DataDirectory, ".instance.lock")));
			}
			finally
			{
				AppRuntime.Reset();
			}
		}

		[TestMethod]
		public void PortableDataLease_ExcludesSecondLeaseUntilDisposed()
		{
			using var directory = new TemporaryDirectory();
			var lease = PortableDataLease.Acquire(directory.Path);

			Assert.ThrowsExactly<IOException>(() => PortableDataLease.Acquire(directory.Path));
			lease.Dispose();

			using var reacquired = PortableDataLease.Acquire(directory.Path);
			Assert.IsTrue(File.Exists(Path.Combine(directory.Path, ".instance.lock")));
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
