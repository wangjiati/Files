// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;
using System.IO;
using ByteSize = ByteSizeLib.ByteSize;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Builds export rows from in-memory listed items or freshly enumerated file system entries.
	/// Name mapping prefers <see cref="ListedItem.ItemNameRaw"/> because <see cref="ListedItem.Name"/>
	/// hides extensions per user settings, which would break exported data fidelity.
	/// </summary>
	public static class FileListExportRowFactory
	{
		public static FileListExportRow FromListedItem(ListedItem item, string rootPath, string? tagNames)
		{
			var fullPath = item.ItemPath ?? string.Empty;

			return new FileListExportRow(
				item.ItemNameRaw ?? item.Name ?? string.Empty,
				item.FileExtension,
				item.ItemType,
				string.IsNullOrEmpty(item.FileSize) ? null : item.FileSizeBytes,
				string.IsNullOrEmpty(item.FileSize) ? null : item.FileSize,
				ToKnownDate(item.ItemDateCreatedReal),
				ToKnownDate(item.ItemDateModifiedReal),
				fullPath,
				GetRelativePath(rootPath, fullPath),
				item.PrimaryItemAttribute == Windows.Storage.StorageItemTypes.Folder,
				item.IsHiddenItem,
				tagNames);
		}

		public static FileListExportRow FromFileSystemInfo(FileSystemInfo info, string rootPath, string? tagNames)
		{
			var isFolder = info is DirectoryInfo;
			long? sizeBytes = null;
			if (info is FileInfo fileInfo)
			{
				try { sizeBytes = fileInfo.Length; }
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}

			var name = info.Name;

			return new FileListExportRow(
				name,
				isFolder ? null : NormalizeExtension(Path.GetExtension(name)),
				null,
				sizeBytes,
				sizeBytes is null ? null : ByteSize.FromBytes(sizeBytes.Value).ToString(),
				ToKnownDate(ToFileDate(info.CreationTime)),
				ToKnownDate(ToFileDate(info.LastWriteTime)),
				info.FullName,
				GetRelativePath(rootPath, info.FullName),
				isFolder,
				(info.Attributes & System.IO.FileAttributes.Hidden) != 0,
				tagNames);
		}

		/// <summary>Relative path prefixed with ".\" so root-level entries keep a visible relative form.</summary>
		public static string GetRelativePath(string rootPath, string fullPath)
			=> "." + Path.DirectorySeparatorChar + Path.GetRelativePath(rootPath, fullPath);

		/// <summary>Local time ISO-style format; unknown dates export as empty.</summary>
		public static string FormatDate(DateTimeOffset? value)
			=> value is { } date ? date.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;

		private static DateTimeOffset? ToKnownDate(DateTimeOffset value)
			=> value == default ? null : value;

		private static DateTimeOffset ToFileDate(DateTime value)
			=> value.Year <= 1601 ? default : new DateTimeOffset(value);

		private static string? NormalizeExtension(string extension)
			=> string.IsNullOrEmpty(extension) ? null : extension;
	}
}
