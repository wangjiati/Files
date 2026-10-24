// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Files.App.Utils.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class ShellIntegrationTakeoverTests
	{
		private const string ExplorerOpenCommand = "\"C:\\Windows\\explorer.exe\" \"%1\"";
		private const string ExplorerDelegateExecute = "{11dbb47c-a525-400b-9e80-a54615a090c0}";

		[TestMethod]
		public void FirstRun_Disable_DoesNotTouchRegistryOrRegistration()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			SeedExplorer(registry);
			var registration = new FakeOpenDialogRegistration(registry);
			var takeover = CreateTakeover(directory, registry, registration);

			Assert.AreEqual(ShellIntegrationOwner.Windows, takeover.GetDefaultFileManagerOwner());
			Assert.AreEqual(ShellIntegrationOwner.None, takeover.GetOpenFileDialogOwner());
			Assert.IsFalse(takeover.SetDefaultFileManager(false));
			Assert.IsFalse(takeover.SetOpenFileDialog(false));

			AssertExplorerValues(registry);
			Assert.AreEqual(0, registration.Calls);
			Assert.IsNull(directory.Store.GetString(ShellIntegrationTakeover.StateKey));
		}

		[TestMethod]
		public void EnableDefaultFileManager_SavesOriginalValuesBeforeWriting()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			SeedExplorer(registry);
			var takeover = CreateTakeover(directory, registry, new FakeOpenDialogRegistration(registry));

			Assert.IsTrue(takeover.SetDefaultFileManager(true));

			AssertInternalLauncher(registry, ShellIntegrationTakeover.FolderOpenCommandKey, withTargetArgument: true);
			AssertInternalLauncher(registry, ShellIntegrationTakeover.FolderExploreCommandKey, withTargetArgument: true);
			AssertInternalLauncher(registry, ShellIntegrationTakeover.NewWindowCommandKey, withTargetArgument: false);

			var state = ReadState(directory);
			Assert.IsNotNull(state.DefaultFileManager);
			var openCommand = FindSavedValue(state, ShellIntegrationTakeover.FolderOpenCommandKey, null);
			Assert.IsTrue(openCommand.Existed);
			Assert.AreEqual(ExplorerOpenCommand, openCommand.Value?.Data);
			var openDelegate = FindSavedValue(state, ShellIntegrationTakeover.FolderOpenCommandKey, "DelegateExecute");
			Assert.IsTrue(openDelegate.Existed);
			Assert.AreEqual(ExplorerDelegateExecute, openDelegate.Value?.Data);
			var newWindowCommand = FindSavedValue(state, ShellIntegrationTakeover.NewWindowCommandKey, null);
			Assert.IsFalse(newWindowCommand.Existed);
		}

		[TestMethod]
		public void EnableDefaultFileManager_WhenStateCannotBeSaved_DoesNotModifyRegistry()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			SeedExplorer(registry);
			var takeover = new ShellIntegrationTakeover(
				registry,
				new RejectingStateStore(),
				CreatePaths(directory),
				new FakeOpenDialogRegistration(registry));

			Assert.ThrowsExactly<IOException>(() => takeover.SetDefaultFileManager(true));

			AssertExplorerValues(registry);
		}

		[TestMethod]
		public void EnableOpenDialog_WhenStateCannotBeSaved_DoesNotRegister()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			var registration = new FakeOpenDialogRegistration(registry);
			var takeover = new ShellIntegrationTakeover(
				registry,
				new RejectingStateStore(),
				CreatePaths(directory),
				registration);

			Assert.ThrowsExactly<IOException>(() => takeover.SetOpenFileDialog(true));

			Assert.AreEqual(0, registration.Calls);
			Assert.IsNull(registry.ReadValue(ShellIntegrationTakeover.OpenDialogClsidKey, null));
			Assert.IsNull(registry.ReadValue(ShellIntegrationTakeover.SaveDialogClsidKey, null));
		}

		[TestMethod]
		public void DisableDefaultFileManager_RestoresOnlyValuesStillOwned()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			SeedExplorer(registry);
			var takeover = CreateTakeover(directory, registry, new FakeOpenDialogRegistration(registry));
			takeover.SetDefaultFileManager(true);

			takeover.SetDefaultFileManager(false);

			AssertExplorerValues(registry);
			Assert.IsNull(registry.ReadValue(ShellIntegrationTakeover.NewWindowCommandKey, null));
			Assert.IsFalse(registry.KeyExists(ShellIntegrationTakeover.NewWindowCommandKey));
			Assert.IsFalse(registry.KeyExists(@"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}"));
			Assert.IsNull(directory.Store.GetString(ShellIntegrationTakeover.StateKey));
		}

		[TestMethod]
		public void DisableDefaultFileManager_AfterExternalChange_DoesNotOverwriteExternalValues()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			SeedExplorer(registry);
			var takeover = CreateTakeover(directory, registry, new FakeOpenDialogRegistration(registry));
			takeover.SetDefaultFileManager(true);

			const string externalCommand = "\"C:\\Other\\Manager.exe\" \"%1\"";
			registry.SetValue(ShellIntegrationTakeover.FolderOpenCommandKey, null, new(externalCommand, RegistryValueKind.String));
			registry.SetValue(ShellIntegrationTakeover.FolderOpenCommandKey, "DelegateExecute", new("{external}", RegistryValueKind.String));

			takeover.SetDefaultFileManager(false);

			Assert.AreEqual(externalCommand, registry.ReadValue(ShellIntegrationTakeover.FolderOpenCommandKey, null)?.Data);
			Assert.AreEqual("{external}", registry.ReadValue(ShellIntegrationTakeover.FolderOpenCommandKey, "DelegateExecute")?.Data);
			AssertExplorerValues(registry, ShellIntegrationTakeover.FolderExploreCommandKey);
			Assert.IsNull(registry.ReadValue(ShellIntegrationTakeover.NewWindowCommandKey, null));
		}

		[TestMethod]
		public void OpenDialogEnableAndDisable_RestoresCapturedRegistryKeys()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			var registration = new FakeOpenDialogRegistration(registry);
			var takeover = CreateTakeover(directory, registry, registration);
			var openKey = ShellIntegrationTakeover.OpenDialogClsidKey;
			var saveKey = ShellIntegrationTakeover.SaveDialogClsidKey;
			registry.SetValue(openKey, null, new("Existing Open Dialog", RegistryValueKind.String));
			registry.SetValue(openKey + "\\InprocServer32", null, new("C:\\Other\\Open.dll", RegistryValueKind.String));

			Assert.IsTrue(takeover.SetOpenFileDialog(true));

			Assert.AreEqual(ShellIntegrationOwner.Internal, takeover.GetOpenFileDialogOwner());
			var state = ReadState(directory);
			Assert.IsNotNull(state.OpenFileDialog?.OpenDialog);
			Assert.IsTrue(state.OpenFileDialog.OpenDialog.Exists);

			Assert.IsTrue(takeover.SetOpenFileDialog(false));

			Assert.AreEqual("Existing Open Dialog", registry.ReadValue(openKey, null)?.Data);
			Assert.AreEqual("C:\\Other\\Open.dll", registry.ReadValue(openKey + "\\InprocServer32", null)?.Data);
			Assert.IsNull(registry.ReadValue(saveKey, null));
			Assert.IsTrue(registration.UnregisterOpenDialogCalls > 0);
			Assert.IsTrue(registration.UnregisterSaveDialogCalls > 0);
			Assert.IsNull(directory.Store.GetString(ShellIntegrationTakeover.StateKey));
		}

		[TestMethod]
		public void OpenDialogDisable_AfterExternalChange_DoesNotOverwriteExternalRegistration()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			var registration = new FakeOpenDialogRegistration(registry);
			var takeover = CreateTakeover(directory, registry, registration);
			var openKey = ShellIntegrationTakeover.OpenDialogClsidKey;
			takeover.SetOpenFileDialog(true);

			registry.SetValue(openKey, null, new("Externally Replaced", RegistryValueKind.String));
			registry.SetValue(openKey + "\\InprocServer32", null, new("C:\\Other\\Open.dll", RegistryValueKind.String));

			takeover.SetOpenFileDialog(false);

			Assert.AreEqual("Externally Replaced", registry.ReadValue(openKey, null)?.Data);
			Assert.AreEqual("C:\\Other\\Open.dll", registry.ReadValue(openKey + "\\InprocServer32", null)?.Data);
			Assert.AreEqual(0, registration.UnregisterOpenDialogCalls);
			Assert.IsTrue(registration.UnregisterSaveDialogCalls > 0);
			Assert.IsNull(directory.Store.GetString(ShellIntegrationTakeover.StateKey));
		}

		[TestMethod]
		public void OwnerDetection_ReportsExternalAndInternalOwnership()
		{
			using var directory = new TemporaryDirectory();
			var registry = new FakeRegistry();
			var takeover = CreateTakeover(directory, registry, new FakeOpenDialogRegistration(registry));

			Assert.AreEqual(ShellIntegrationOwner.Windows, takeover.GetDefaultFileManagerOwner());
			registry.SetValue(ShellIntegrationTakeover.FolderOpenCommandKey, null, new("C:\\Other\\Manager.exe \"%1\"", RegistryValueKind.String));
			Assert.AreEqual(ShellIntegrationOwner.Other, takeover.GetDefaultFileManagerOwner());

			takeover.SetDefaultFileManager(true);
			Assert.AreEqual(ShellIntegrationOwner.Internal, takeover.GetDefaultFileManagerOwner());
		}

		private static ShellIntegrationTakeover CreateTakeover(
			TemporaryDirectory directory,
			FakeRegistry registry,
			FakeOpenDialogRegistration registration)
			=> new(registry, directory.Store, CreatePaths(directory), registration);

		private static ShellIntegrationPaths CreatePaths(TemporaryDirectory directory)
			=> new(Path.Combine(directory.Path, "Files.Internal"), Path.Combine(directory.Path, "Files.Internal", "Files.App.Launcher.exe"));

		private static void SeedExplorer(FakeRegistry registry)
		{
			registry.SetValue(ShellIntegrationTakeover.FolderOpenCommandKey, null, new(ExplorerOpenCommand, RegistryValueKind.ExpandString));
			registry.SetValue(ShellIntegrationTakeover.FolderOpenCommandKey, "DelegateExecute", new(ExplorerDelegateExecute, RegistryValueKind.String));
			registry.SetValue(ShellIntegrationTakeover.FolderExploreCommandKey, null, new(ExplorerOpenCommand, RegistryValueKind.ExpandString));
			registry.SetValue(ShellIntegrationTakeover.FolderExploreCommandKey, "DelegateExecute", new(ExplorerDelegateExecute, RegistryValueKind.String));
		}

		private static void AssertExplorerValues(FakeRegistry registry, string? keyPath = null)
		{
			var keys = keyPath is null
				? new[] { ShellIntegrationTakeover.FolderOpenCommandKey, ShellIntegrationTakeover.FolderExploreCommandKey }
				: new[] { keyPath };
			foreach (var key in keys)
			{
				Assert.AreEqual(ExplorerOpenCommand, registry.ReadValue(key, null)?.Data);
				Assert.AreEqual(ExplorerDelegateExecute, registry.ReadValue(key, "DelegateExecute")?.Data);
			}
		}

		private static void AssertInternalLauncher(FakeRegistry registry, string keyPath, bool withTargetArgument)
		{
			var command = registry.ReadValue(keyPath, null)?.Data;
			Assert.IsNotNull(command);
			Assert.IsTrue(command.Contains("Files.App.Launcher.exe", StringComparison.OrdinalIgnoreCase));
			Assert.AreEqual(withTargetArgument, command.Contains("\"%1\"", StringComparison.OrdinalIgnoreCase));
			Assert.AreEqual(string.Empty, registry.ReadValue(keyPath, "DelegateExecute")?.Data);
		}

		private static ShellIntegrationTakeoverState ReadState(TemporaryDirectory directory)
		{
			var json = directory.Store.GetString(ShellIntegrationTakeover.StateKey);
			Assert.IsNotNull(json);
			var state = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ShellIntegrationTakeoverState);
			Assert.IsNotNull(state);
			return state;
		}

		private static ShellRegistryValueState FindSavedValue(ShellIntegrationTakeoverState state, string keyPath, string? valueName)
			=> state.DefaultFileManager!.Values.Single(value =>
				value.KeyPath == keyPath && string.Equals(value.ValueName, valueName, StringComparison.Ordinal));

		private sealed class FakeRegistry : IShellIntegrationRegistry
		{
			private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, Dictionary<string, ShellRegistryValue>> _values = new(StringComparer.OrdinalIgnoreCase);

			public ShellRegistryValue? ReadValue(string keyPath, string? valueName)
			{
				if (!_keys.Contains(keyPath) || !_values.TryGetValue(keyPath, out var values))
					return null;

				return values.TryGetValue(valueName ?? string.Empty, out var value) ? value : null;
			}

			public void SetValue(string keyPath, string? valueName, ShellRegistryValue value)
			{
				_keys.Add(keyPath);
				if (!_values.TryGetValue(keyPath, out var values))
				{
					values = new(StringComparer.OrdinalIgnoreCase);
					_values[keyPath] = values;
				}

				values[valueName ?? string.Empty] = value;
			}

			public void DeleteValue(string keyPath, string? valueName)
			{
				if (_values.TryGetValue(keyPath, out var values))
					values.Remove(valueName ?? string.Empty);
			}

			public bool KeyExists(string keyPath)
				=> _keys.Contains(keyPath) ||
					_keys.Any(key => key.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase));

			public ShellRegistryKeySnapshot ReadKey(string keyPath)
			{
				var exists = KeyExists(keyPath);
				var snapshot = new ShellRegistryKeySnapshot { Exists = exists };
				if (!exists)
					return snapshot;

				if (_values.TryGetValue(keyPath, out var values))
				{
					foreach (var (name, value) in values)
						snapshot.Values.Add(new(name.Length == 0 ? null : name, value));
				}

				foreach (var subKey in _keys.Where(key =>
					key.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase) &&
					!key[(keyPath.Length + 1)..].Contains('\\')))
				{
					snapshot.SubKeys[subKey[(keyPath.Length + 1)..]] = ReadKey(subKey);
				}

				return snapshot;
			}

			public void DeleteKey(string keyPath)
			{
				foreach (var key in _keys.Where(key =>
					key.Equals(keyPath, StringComparison.OrdinalIgnoreCase) ||
					key.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
				{
					_keys.Remove(key);
					_values.Remove(key);
				}
			}

			public void DeleteKeyIfEmpty(string keyPath, string stopAtKeyPath)
			{
				var current = keyPath;
				while (current.StartsWith(stopAtKeyPath + "\\", StringComparison.OrdinalIgnoreCase))
				{
					var hasValues = _values.TryGetValue(current, out var values) && values.Count > 0;
					var hasSubKeys = _keys.Any(key => key.StartsWith(current + "\\", StringComparison.OrdinalIgnoreCase));
					if (hasValues || hasSubKeys)
						return;

					_keys.Remove(current);
					_values.Remove(current);
					var separator = current.LastIndexOf('\\');
					if (separator <= 0)
						return;

					current = current[..separator];
				}
			}

			public void RestoreKey(string keyPath, ShellRegistryKeySnapshot snapshot)
			{
				DeleteKey(keyPath);
				if (!snapshot.Exists)
					return;

				_keys.Add(keyPath);
				if (!_values.ContainsKey(keyPath))
					_values[keyPath] = new(StringComparer.OrdinalIgnoreCase);

				foreach (var value in snapshot.Values)
					SetValue(keyPath, value.Name, value.Value);
				foreach (var (name, subKey) in snapshot.SubKeys)
					RestoreKey(keyPath + "\\" + name, subKey);
			}
		}

		private sealed class FakeOpenDialogRegistration : IOpenDialogRegistration
		{
			private readonly FakeRegistry _registry;

			public FakeOpenDialogRegistration(FakeRegistry registry)
			{
				_registry = registry;
			}

			public int Calls { get; private set; }
			public int UnregisterOpenDialogCalls { get; private set; }
			public int UnregisterSaveDialogCalls { get; private set; }

			public void RegisterOpenDialog(string integrationDirectory)
			{
				Calls++;
				WriteClsid(ShellIntegrationTakeover.OpenDialogClsidKey, ShellIntegrationTakeover.OpenDialogClass, "Files.App.OpenDialog64.dll", integrationDirectory);
			}

			public void RegisterSaveDialog(string integrationDirectory)
			{
				Calls++;
				WriteClsid(ShellIntegrationTakeover.SaveDialogClsidKey, ShellIntegrationTakeover.SaveDialogClass, "Files.App.SaveDialog64.dll", integrationDirectory);
			}

			public void UnregisterOpenDialog(string integrationDirectory)
			{
				Calls++;
				UnregisterOpenDialogCalls++;
				_registry.DeleteKey(ShellIntegrationTakeover.OpenDialogClsidKey);
			}

			public void UnregisterSaveDialog(string integrationDirectory)
			{
				Calls++;
				UnregisterSaveDialogCalls++;
				_registry.DeleteKey(ShellIntegrationTakeover.SaveDialogClsidKey);
			}

			private void WriteClsid(string keyPath, string className, string dllName, string integrationDirectory)
			{
				_registry.SetValue(keyPath, null, new(className, RegistryValueKind.String));
				_registry.SetValue(keyPath + "\\InprocServer32", null, new(Path.Combine(integrationDirectory, dllName), RegistryValueKind.String));
				_registry.SetValue(keyPath + "\\InprocServer32", "ThreadingModel", new("Apartment", RegistryValueKind.String));
			}
		}

		private sealed class RejectingStateStore : ILocalStateStore
		{
			public bool GetBoolean(string key, bool defaultValue = false) => defaultValue;
			public void SetBoolean(string key, bool value) => throw new IOException("State storage is unavailable.");
			public int GetInt32(string key, int defaultValue = 0) => defaultValue;
			public void SetInt32(string key, int value) => throw new IOException("State storage is unavailable.");
			public double GetDouble(string key, double defaultValue = 0d) => defaultValue;
			public void SetDouble(string key, double value) => throw new IOException("State storage is unavailable.");
			public string? GetString(string key, string? defaultValue = null) => defaultValue;
			public void SetString(string key, string value) => throw new IOException("State storage is unavailable.");
			public void Remove(string key) => throw new IOException("State storage is unavailable.");
		}

		private sealed class TemporaryDirectory : IDisposable
		{
			public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Files.Tests.{Guid.NewGuid():N}");

			public JsonLocalStateStore Store { get; }

			public TemporaryDirectory()
			{
				Directory.CreateDirectory(Path);
				Store = new JsonLocalStateStore(System.IO.Path.Combine(Path, "local-state.json"));
			}

			public void Dispose()
			{
				try
				{
					Directory.Delete(Path, true);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
	}
}
