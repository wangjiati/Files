// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Win32;
using System.Globalization;

namespace Files.App.Services
{
	internal sealed class WindowsShellIntegrationRegistry : IShellIntegrationRegistry
	{
		public ShellRegistryValue? ReadValue(string keyPath, string? valueName)
		{
			using var key = Registry.CurrentUser.OpenSubKey(keyPath);
			if (key is null)
				return null;

			var name = valueName ?? string.Empty;
			if (!key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
				return null;

			var kind = key.GetValueKind(name);
			var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
			return new(ToStoredString(data, kind), kind);
		}

		public void SetValue(string keyPath, string? valueName, ShellRegistryValue value)
		{
			using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
			key.SetValue(valueName ?? string.Empty, ToRegistryValue(value), value.Kind);
		}

		public void DeleteValue(string keyPath, string? valueName)
		{
			using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
			key?.DeleteValue(valueName ?? string.Empty, false);
		}

		public ShellRegistryKeySnapshot ReadKey(string keyPath)
		{
			using var key = Registry.CurrentUser.OpenSubKey(keyPath);
			if (key is null)
				return new() { Exists = false };

			return ReadKey(key);
		}

		public void DeleteKey(string keyPath)
			=> Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);

		public void DeleteKeyIfEmpty(string keyPath, string stopAtKeyPath)
		{
			var current = keyPath;
			while (current.StartsWith(stopAtKeyPath + "\\", StringComparison.OrdinalIgnoreCase))
			{
				using (var key = Registry.CurrentUser.OpenSubKey(current, true))
				{
					if (key is null || key.ValueCount != 0 || key.SubKeyCount != 0)
						return;
				}

				Registry.CurrentUser.DeleteSubKeyTree(current, false);
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

			WriteKey(keyPath, snapshot);
		}

		private static ShellRegistryKeySnapshot ReadKey(RegistryKey key)
		{
			var snapshot = new ShellRegistryKeySnapshot { Exists = true };
			foreach (var name in key.GetValueNames())
			{
				var kind = key.GetValueKind(name);
				var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
				snapshot.Values.Add(new(name.Length == 0 ? null : name, new(ToStoredString(data, kind), kind)));
			}

			foreach (var subKeyName in key.GetSubKeyNames())
			{
				using var subKey = key.OpenSubKey(subKeyName);
				if (subKey is not null)
					snapshot.SubKeys[subKeyName] = ReadKey(subKey);
			}

			return snapshot;
		}

		private static void WriteKey(string keyPath, ShellRegistryKeySnapshot snapshot)
		{
			using (var key = Registry.CurrentUser.CreateSubKey(keyPath, true))
			{
				foreach (var value in snapshot.Values)
					key.SetValue(value.Name ?? string.Empty, ToRegistryValue(value.Value), value.Value.Kind);
			}

			foreach (var (name, subKey) in snapshot.SubKeys)
				WriteKey(keyPath + "\\" + name, subKey);
		}

		private static string ToStoredString(object? value, RegistryValueKind kind)
			=> kind switch
			{
				RegistryValueKind.String or RegistryValueKind.ExpandString => value as string ?? string.Empty,
				RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
				RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
				RegistryValueKind.Binary => Convert.ToBase64String((byte[])(value ?? Array.Empty<byte>())),
				_ => throw new NotSupportedException($"Registry value kind '{kind}' cannot be preserved."),
			};

		private static object ToRegistryValue(ShellRegistryValue value)
			=> value.Kind switch
			{
				RegistryValueKind.String or RegistryValueKind.ExpandString => value.Data ?? string.Empty,
				RegistryValueKind.DWord => int.Parse(value.Data ?? "0", CultureInfo.InvariantCulture),
				RegistryValueKind.QWord => long.Parse(value.Data ?? "0", CultureInfo.InvariantCulture),
				RegistryValueKind.Binary => Convert.FromBase64String(value.Data ?? string.Empty),
				_ => throw new NotSupportedException($"Registry value kind '{value.Kind}' cannot be restored."),
			};
	}
}
