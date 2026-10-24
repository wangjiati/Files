// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Win32;
using System.IO;

namespace Files.App.Services
{
	internal enum ShellIntegrationOwner
	{
		None,
		Windows,
		Internal,
		Other,
	}

	internal sealed record ShellIntegrationPaths(string IntegrationDirectory, string LauncherPath)
	{
		private const string IntegrationDirectoryName = "Files.Internal";

		public static ShellIntegrationPaths CreateDefault()
		{
			var directory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				IntegrationDirectoryName);
			return new(directory, Path.Combine(directory, "Files.App.Launcher.exe"));
		}
	}

	internal sealed record ShellRegistryValue(string? Data, RegistryValueKind Kind);

	internal sealed record ShellRegistryNamedValue(string? Name, ShellRegistryValue Value);

	internal sealed class ShellRegistryKeySnapshot
	{
		public bool Exists { get; set; }
		public List<ShellRegistryNamedValue> Values { get; set; } = [];
		public Dictionary<string, ShellRegistryKeySnapshot> SubKeys { get; set; } = [];
	}

	internal sealed class ShellRegistryValueState
	{
		public string KeyPath { get; set; } = string.Empty;
		public string? ValueName { get; set; }
		public bool Existed { get; set; }
		public ShellRegistryValue? Value { get; set; }
	}

	internal sealed class DefaultFileManagerTakeoverState
	{
		public List<ShellRegistryValueState> Values { get; set; } = [];
		public List<string> UnknownOriginalKeyPaths { get; set; } = [];
	}

	internal sealed class OpenFileDialogTakeoverState
	{
		public ShellRegistryKeySnapshot? OpenDialog { get; set; }
		public ShellRegistryKeySnapshot? SaveDialog { get; set; }
	}

	internal sealed class ShellIntegrationTakeoverState
	{
		public int SchemaVersion { get; set; } = ShellIntegrationTakeover.CurrentSchemaVersion;
		public DefaultFileManagerTakeoverState? DefaultFileManager { get; set; }
		public OpenFileDialogTakeoverState? OpenFileDialog { get; set; }
	}

	internal interface IShellIntegrationRegistry
	{
		ShellRegistryValue? ReadValue(string keyPath, string? valueName);
		void SetValue(string keyPath, string? valueName, ShellRegistryValue value);
		void DeleteValue(string keyPath, string? valueName);
		ShellRegistryKeySnapshot ReadKey(string keyPath);
		void DeleteKey(string keyPath);
		void DeleteKeyIfEmpty(string keyPath, string stopAtKeyPath);
		void RestoreKey(string keyPath, ShellRegistryKeySnapshot snapshot);
	}

	internal interface IOpenDialogRegistration
	{
		void RegisterOpenDialog(string integrationDirectory);
		void RegisterSaveDialog(string integrationDirectory);
		void UnregisterOpenDialog(string integrationDirectory);
		void UnregisterSaveDialog(string integrationDirectory);
	}

	internal sealed class ShellIntegrationTakeover
	{
		internal const string StateKey = "shell-integration-takeover.v1";
		internal const string ArchivedStateKey = "shell-integration-takeover.v1.archived";
		internal const int CurrentSchemaVersion = 1;

		internal const string FolderOpenCommandKey = @"Software\Classes\Folder\shell\open\command";
		internal const string FolderExploreCommandKey = @"Software\Classes\Folder\shell\explore\command";
		internal const string NewWindowCommandKey = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command";
		internal const string OpenDialogClsidKey = @"Software\Classes\CLSID\{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}";
		internal const string SaveDialogClsidKey = @"Software\Classes\CLSID\{C0B4E2F3-BA21-4773-8DBA-335EC946EB8B}";
		internal const string OpenDialogClass = "FilesOpenDialog class";
		internal const string SaveDialogClass = "FilesSaveDialog class";

		private const string DelegateExecuteValueName = "DelegateExecute";
		private const string InprocServer32SubKey = "InprocServer32";

		private static readonly (string KeyPath, bool HasTargetArgument, string StopAtKeyPath)[] LauncherCommandKeys =
		[
			(FolderOpenCommandKey, true, @"Software\Classes\Folder\shell"),
			(FolderExploreCommandKey, true, @"Software\Classes\Folder\shell"),
			(NewWindowCommandKey, false, @"Software\Classes\CLSID"),
		];

		private readonly IShellIntegrationRegistry _registry;
		private readonly ILocalStateStore _stateStore;
		private readonly ShellIntegrationPaths _paths;
		private readonly IOpenDialogRegistration _openDialogRegistration;

		public ShellIntegrationTakeover(
			IShellIntegrationRegistry registry,
			ILocalStateStore stateStore,
			ShellIntegrationPaths paths,
			IOpenDialogRegistration openDialogRegistration)
		{
			_registry = registry;
			_stateStore = stateStore;
			_paths = paths;
			_openDialogRegistration = openDialogRegistration;
		}

		public ShellIntegrationOwner GetDefaultFileManagerOwner()
		{
			var command = _registry.ReadValue(FolderOpenCommandKey, null)?.Data;
			if (IsInternalLauncherCommand(command))
				return ShellIntegrationOwner.Internal;
			if (string.IsNullOrWhiteSpace(command) ||
				command.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase))
				return ShellIntegrationOwner.Windows;

			return ShellIntegrationOwner.Other;
		}

		public ShellIntegrationOwner GetOpenFileDialogOwner()
		{
			var openDialogOwned = IsClsidOwned(OpenDialogClsidKey, OpenDialogClass);
			var saveDialogOwned = IsClsidOwned(SaveDialogClsidKey, SaveDialogClass);
			if (openDialogOwned && saveDialogOwned)
				return ShellIntegrationOwner.Internal;

			var anyRegistrationPresent =
				_registry.ReadValue(OpenDialogClsidKey, null) is not null ||
				_registry.ReadValue(SaveDialogClsidKey, null) is not null;
			return anyRegistrationPresent ? ShellIntegrationOwner.Other : ShellIntegrationOwner.None;
		}

		public bool SetDefaultFileManager(bool enabled)
			=> enabled ? EnableDefaultFileManager() : DisableDefaultFileManager();

		public bool SetOpenFileDialog(bool enabled)
			=> enabled ? EnableOpenFileDialog() : DisableOpenFileDialog();

		private bool EnableDefaultFileManager()
		{
			var state = LoadState();
			if (LauncherCommandKeys.All(key => IsLauncherCommandGroupOwned(key.KeyPath)))
				return false;

			var existingValues = state.DefaultFileManager?.Values;
			var (values, unknownOriginalKeyPaths) = SnapshotDefaultFileManagerValues(existingValues);
			state.DefaultFileManager = new DefaultFileManagerTakeoverState
			{
				Values = values,
				UnknownOriginalKeyPaths = unknownOriginalKeyPaths,
			};
			SaveState(state);

			try
			{
				foreach (var (keyPath, hasTargetArgument, _) in LauncherCommandKeys)
				{
					_registry.SetValue(keyPath, null, new(BuildLauncherCommand(hasTargetArgument), RegistryValueKind.String));
					_registry.SetValue(keyPath, DelegateExecuteValueName, new(string.Empty, RegistryValueKind.String));
				}
			}
			catch
			{
				RestoreDefaultFileManagerValues(state.DefaultFileManager, requireOwnership: false);
				state.DefaultFileManager = null;
				SaveState(state);
				throw;
			}

			return true;
		}

		private bool DisableDefaultFileManager()
		{
			var state = LoadState();
			if (state.DefaultFileManager is { } savedState)
			{
				RestoreDefaultFileManagerValues(savedState, requireOwnership: true);
				state.DefaultFileManager = null;
				SaveState(state);
				return true;
			}

			var removedOwnedValues = false;
			foreach (var (keyPath, _, stopAtKeyPath) in LauncherCommandKeys)
			{
				if (!IsLauncherCommandGroupOwned(keyPath))
					continue;

				DeleteOwnedLauncherCommandValues(keyPath, stopAtKeyPath);
				removedOwnedValues = true;
			}

			return removedOwnedValues;
		}

		private (List<ShellRegistryValueState> Values, List<string> UnknownOriginalKeyPaths) SnapshotDefaultFileManagerValues(
			List<ShellRegistryValueState>? existingValues)
		{
			var values = new List<ShellRegistryValueState>();
			var unknownOriginalKeyPaths = new List<string>();
			foreach (var (keyPath, _, _) in LauncherCommandKeys)
			{
				var isOwned = IsLauncherCommandGroupOwned(keyPath);
				var existingGroup = isOwned
					? existingValues?.Where(value => value.KeyPath == keyPath).ToList()
					: null;

				if (existingGroup is { Count: > 0 })
				{
					values.AddRange(existingGroup);
					continue;
				}

				if (isOwned)
				{
					unknownOriginalKeyPaths.Add(keyPath);
					continue;
				}

				foreach (var valueName in new string?[] { null, DelegateExecuteValueName })
				{
					var value = _registry.ReadValue(keyPath, valueName);
					values.Add(new ShellRegistryValueState
					{
						KeyPath = keyPath,
						ValueName = valueName,
						Existed = value is not null,
						Value = value,
					});
				}
			}

			return (values, unknownOriginalKeyPaths);
		}

		private void RestoreDefaultFileManagerValues(DefaultFileManagerTakeoverState savedState, bool requireOwnership)
		{
			foreach (var group in savedState.Values.GroupBy(value => value.KeyPath))
			{
				if (requireOwnership && !IsLauncherCommandGroupOwned(group.Key))
					continue;

				foreach (var value in group)
				{
					if (value.Existed && value.Value is not null)
						_registry.SetValue(value.KeyPath, value.ValueName, value.Value);
					else
						_registry.DeleteValue(value.KeyPath, value.ValueName);
				}

				if (group.Any(value => !value.Existed))
				{
					var stopAtKeyPath = LauncherCommandKeys
						.First(command => command.KeyPath == group.Key)
						.StopAtKeyPath;
					_registry.DeleteKeyIfEmpty(group.Key, stopAtKeyPath);
				}
			}

			foreach (var keyPath in savedState.UnknownOriginalKeyPaths)
			{
				if (!IsLauncherCommandGroupOwned(keyPath))
					continue;

				var stopAtKeyPath = LauncherCommandKeys
					.First(command => command.KeyPath == keyPath)
					.StopAtKeyPath;
				DeleteOwnedLauncherCommandValues(keyPath, stopAtKeyPath);
			}
		}

		private void DeleteOwnedLauncherCommandValues(string keyPath, string stopAtKeyPath)
		{
			_registry.DeleteValue(keyPath, null);
			_registry.DeleteValue(keyPath, DelegateExecuteValueName);
			_registry.DeleteKeyIfEmpty(keyPath, stopAtKeyPath);
		}

		private bool EnableOpenFileDialog()
		{
			var state = LoadState();
			var openDialogOwned = IsClsidOwned(OpenDialogClsidKey, OpenDialogClass);
			var saveDialogOwned = IsClsidOwned(SaveDialogClsidKey, SaveDialogClass);
			if (openDialogOwned && saveDialogOwned)
				return false;

			state.OpenFileDialog ??= new OpenFileDialogTakeoverState();
			if (!openDialogOwned)
				state.OpenFileDialog.OpenDialog = _registry.ReadKey(OpenDialogClsidKey);
			if (!saveDialogOwned)
				state.OpenFileDialog.SaveDialog = _registry.ReadKey(SaveDialogClsidKey);
			SaveState(state);

			if (!openDialogOwned)
				_openDialogRegistration.RegisterOpenDialog(_paths.IntegrationDirectory);
			if (!saveDialogOwned)
				_openDialogRegistration.RegisterSaveDialog(_paths.IntegrationDirectory);

			return true;
		}

		private bool DisableOpenFileDialog()
		{
			var state = LoadState();
			if (state.OpenFileDialog is not { } savedState)
			{
				var removedOwnedRegistration = false;
				if (IsClsidOwned(OpenDialogClsidKey, OpenDialogClass))
				{
					_openDialogRegistration.UnregisterOpenDialog(_paths.IntegrationDirectory);
					_registry.DeleteKey(OpenDialogClsidKey);
					removedOwnedRegistration = true;
				}

				if (IsClsidOwned(SaveDialogClsidKey, SaveDialogClass))
				{
					_openDialogRegistration.UnregisterSaveDialog(_paths.IntegrationDirectory);
					_registry.DeleteKey(SaveDialogClsidKey);
					removedOwnedRegistration = true;
				}

				return removedOwnedRegistration;
			}

			if (IsClsidOwned(OpenDialogClsidKey, OpenDialogClass))
			{
				_openDialogRegistration.UnregisterOpenDialog(_paths.IntegrationDirectory);
				if (savedState.OpenDialog is { } openDialogSnapshot)
					_registry.RestoreKey(OpenDialogClsidKey, openDialogSnapshot);
				else
					_registry.DeleteKey(OpenDialogClsidKey);
			}

			if (IsClsidOwned(SaveDialogClsidKey, SaveDialogClass))
			{
				_openDialogRegistration.UnregisterSaveDialog(_paths.IntegrationDirectory);
				if (savedState.SaveDialog is { } saveDialogSnapshot)
					_registry.RestoreKey(SaveDialogClsidKey, saveDialogSnapshot);
				else
					_registry.DeleteKey(SaveDialogClsidKey);
			}

			state.OpenFileDialog = null;
			SaveState(state);
			return true;
		}

		private bool IsLauncherCommandGroupOwned(string keyPath)
		{
			var command = _registry.ReadValue(keyPath, null)?.Data;
			if (!IsInternalLauncherCommand(command))
				return false;

			return string.Equals(
				_registry.ReadValue(keyPath, DelegateExecuteValueName)?.Data,
				string.Empty,
				StringComparison.Ordinal);
		}

		private bool IsClsidOwned(string keyPath, string expectedClassName)
		{
			if (!string.Equals(_registry.ReadValue(keyPath, null)?.Data, expectedClassName, StringComparison.Ordinal))
				return false;

			var serverPath = _registry.ReadValue(keyPath + "\\" + InprocServer32SubKey, null)?.Data;
			if (string.IsNullOrWhiteSpace(serverPath))
				return false;

			var serverDirectory = Path.GetDirectoryName(serverPath);
			return string.Equals(
				Path.TrimEndingDirectorySeparator(serverDirectory ?? string.Empty),
				Path.TrimEndingDirectorySeparator(_paths.IntegrationDirectory),
				StringComparison.OrdinalIgnoreCase);
		}

		private bool IsInternalLauncherCommand(string? command)
			=> !string.IsNullOrWhiteSpace(command) &&
				command.Contains(_paths.LauncherPath, StringComparison.OrdinalIgnoreCase);

		private string BuildLauncherCommand(bool hasTargetArgument)
			=> hasTargetArgument
				? $"\"{_paths.LauncherPath}\" \"%1\""
				: $"\"{_paths.LauncherPath}\"";

		private ShellIntegrationTakeoverState LoadState()
		{
			var json = _stateStore.GetString(StateKey);
			if (string.IsNullOrWhiteSpace(json))
				return new();

			try
			{
				var state = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ShellIntegrationTakeoverState);
				if (state is null || state.SchemaVersion != CurrentSchemaVersion || !IsStateUsable(state))
					throw new InvalidDataException("The shell integration takeover state schema is not supported.");

				return state;
			}
			catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
			{
				ArchiveState(json);
				return new();
			}
		}

		private static bool IsStateUsable(ShellIntegrationTakeoverState state)
		{
			if (state.DefaultFileManager is { } defaultFileManager)
			{
				if (defaultFileManager.Values is null || defaultFileManager.UnknownOriginalKeyPaths is null)
					return false;

				if (defaultFileManager.Values.Any(value =>
					!LauncherCommandKeys.Any(command => command.KeyPath == value.KeyPath) ||
					(value.ValueName is not null && value.ValueName != DelegateExecuteValueName) ||
					(value.Value is not null && !IsSupportedValueKind(value.Value.Kind))))
					return false;

				if (defaultFileManager.UnknownOriginalKeyPaths.Any(keyPath =>
					!LauncherCommandKeys.Any(command => command.KeyPath == keyPath)))
					return false;
			}

			return IsSnapshotUsable(state.OpenFileDialog?.OpenDialog) &&
				IsSnapshotUsable(state.OpenFileDialog?.SaveDialog);
		}

		private static bool IsSnapshotUsable(ShellRegistryKeySnapshot? snapshot)
		{
			if (snapshot is null)
				return true;

			if (snapshot.Values is null || snapshot.SubKeys is null)
				return false;

			if (snapshot.Values.Any(value => value is null || value.Value is null || !IsSupportedValueKind(value.Value.Kind)))
				return false;

			foreach (var (name, subKey) in snapshot.SubKeys)
			{
				if (string.IsNullOrEmpty(name) || name.Contains('\\') || name is "." or "..")
					return false;
				if (subKey is null || !IsSnapshotUsable(subKey))
					return false;
			}

			return true;
		}

		private static bool IsSupportedValueKind(RegistryValueKind kind)
			=> kind is RegistryValueKind.String or RegistryValueKind.ExpandString or
				RegistryValueKind.Binary or RegistryValueKind.DWord or RegistryValueKind.QWord;

		private void ArchiveState(string json)
		{
			// Archiving is best effort; a broken state store must not prevent releasing an owned takeover.
			try
			{
				_stateStore.SetString(ArchivedStateKey, json);
			}
			catch (Exception)
			{
			}

			try
			{
				_stateStore.Remove(StateKey);
			}
			catch (Exception)
			{
			}
		}

		private void SaveState(ShellIntegrationTakeoverState state)
		{
			if (state.DefaultFileManager is null && state.OpenFileDialog is null)
			{
				_stateStore.Remove(StateKey);
				return;
			}

			var json = JsonSerializer.Serialize(state, AppJsonSerializerContext.Default.ShellIntegrationTakeoverState);
			_stateStore.SetString(StateKey, json);
		}
	}
}
