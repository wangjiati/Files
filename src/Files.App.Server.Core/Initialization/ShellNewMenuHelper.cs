// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Text;
using Windows.Storage;

namespace Files.App.Server.Core
{
	/// <summary>
	/// Provides static helper to get extension-specific shell context menu from Windows Registry.
	/// </summary>
	internal static class ShellNewMenuHelper
	{
		public static async Task<List<ShellNewEntry>> GetNewContextMenuEntries(string cacheDirectory)
		{
			var newMenuItems = new List<ShellNewEntry>();

			var shortcutExtensions = new string[] { ShellLibraryItem.EXTENSION, ".url", ".lnk" };

			foreach (var keyName in Registry.ClassesRoot.GetSubKeyNames().Where(x => x.StartsWith('.') && !shortcutExtensions.Contains(x, StringComparer.OrdinalIgnoreCase)))
			{
				using var key = Registry.ClassesRoot.OpenSubKeySafe(keyName);

				if (key is not null)
				{
					var ret = await GetShellNewRegistryEntries(key, key, cacheDirectory);
					if (ret is not null)
						newMenuItems.Add(ret);
				}
			}

			if (!newMenuItems.Any(x => ".txt".Equals(x.Extension, StringComparison.OrdinalIgnoreCase)))
				newMenuItems.Add(await CreateShellNewEntry(".txt", null, null, null, cacheDirectory));

			return [.. newMenuItems.OrderBy(item => item.Name)];
		}

		public static async Task<ShellNewEntry?> GetNewContextMenuEntryForType(string extension, string cacheDirectory)
		{
			if (string.IsNullOrEmpty(extension))
				return null;

			using var key = Registry.ClassesRoot.OpenSubKeySafe(extension);

			return key is not null ? await GetShellNewRegistryEntries(key, key, cacheDirectory) : null;
		}

		private static async Task<ShellNewEntry?> GetShellNewRegistryEntries(RegistryKey current, RegistryKey root, string cacheDirectory)
		{
			try
			{
				foreach (var keyName in current.GetSubKeyNames())
				{
					using var key = current.OpenSubKeySafe(keyName);

					if (key is null)
						continue;

					if (keyName == "ShellNew")
						return await ParseShellNewRegistryEntry(key, root, cacheDirectory);
					else
					{
						var ret = await GetShellNewRegistryEntries(key, root, cacheDirectory);
						if (ret is not null)
							return ret;
					}
				}
			}
			catch
			{
				// Ignore exceptions when the registry is inaccessible to avoid freezes
			}

			return null;
		}

		private static async Task<ShellNewEntry?> ParseShellNewRegistryEntry(RegistryKey key, RegistryKey root, string cacheDirectory)
		{
			var valueNames = key.GetValueNames();

			if (!valueNames.Contains("NullFile", StringComparer.OrdinalIgnoreCase) &&
				!valueNames.Contains("Name", StringComparer.OrdinalIgnoreCase) &&
				!valueNames.Contains("FileName", StringComparer.OrdinalIgnoreCase) &&
				!valueNames.Contains("Command", StringComparer.OrdinalIgnoreCase) &&
				!valueNames.Contains("ItemName", StringComparer.OrdinalIgnoreCase) &&
				!valueNames.Contains("Data", StringComparer.OrdinalIgnoreCase))
			{
				return null;
			}

			var extension = root.Name.Substring(root.Name.LastIndexOf('\\') + 1);
			var fileName = key.GetValue("FileName") as string;
			var command = key.GetValue("Command") as string;

			byte[]? data = null;
			var dataObj = key.GetValue("Data");

			if (dataObj is not null)
			{
				data = key.GetValueKind("Data") switch
				{
					RegistryValueKind.Binary => (byte[])dataObj,
					RegistryValueKind.String => Encoding.UTF8.GetBytes((string)dataObj),
					_ => (byte[])dataObj,
				};
			}

			return await CreateShellNewEntry(extension, fileName, command, data, cacheDirectory);
		}

		private static async Task<ShellNewEntry> CreateShellNewEntry(string extension, string? fileName, string? command, byte[]? data, string cacheDirectory)
		{
			Directory.CreateDirectory(cacheDirectory);
			var cache = await SafetyExtensions.IgnoreExceptions(() => StorageFolder.GetFolderFromPathAsync(cacheDirectory).AsTask());
			var folder = cache is not null ? await SafetyExtensions.IgnoreExceptions(() => cache.CreateFolderAsync("extensions", CreationCollisionOption.OpenIfExists).AsTask()) : null;
			var sampleFile = folder is not null ? await SafetyExtensions.IgnoreExceptions(() => folder.CreateFileAsync("file" + extension, CreationCollisionOption.OpenIfExists).AsTask()) : null;

			var displayType = sampleFile is not null ? sampleFile.DisplayType : $"file {extension}";
			using var thumbnail = sampleFile is not null ? await SafetyExtensions.IgnoreExceptions(() => sampleFile.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.ListView, 24, Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale).AsTask()) : null;

			byte[]? iconData = null;

			if (thumbnail is not null)
			{
				using var readStream = thumbnail.AsStreamForRead();
				var bitmapData = new byte[readStream.Length];
				await readStream.ReadExactlyAsync(bitmapData, 0, bitmapData.Length);
				iconData = bitmapData;
			}

			var entry = new ShellNewEntry()
			{
				Extension = extension,
				Template = fileName,
				Name = displayType,
				Command = command,
				IconData = iconData,
				Data = data
			};

			return entry;
		}

		private static RegistryKey? OpenSubKeySafe(this RegistryKey root, string keyName)
		{
			try
			{
				return root.OpenSubKey(keyName);
			}
			catch (SecurityException)
			{
				return null;
			}
		}
	}
}
