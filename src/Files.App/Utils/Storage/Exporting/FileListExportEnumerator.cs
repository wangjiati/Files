// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Produces export rows for the current level (in-memory items, view order) and for
	/// recursive export (disk walk, per-level view sorting, shared filter evaluation).
	/// Reparse-point directories are emitted but never traversed, so junction cycles
	/// cannot loop the walk. A folder filtered out of the rows is still walked.
	/// </summary>
	public static class FileListExportEnumerator
	{
		public static IEnumerable<FileListExportRow> EnumerateCurrentLevel(
			IReadOnlyList<ListedItem> items,
			string rootPath,
			Func<ListedItem, string?> tagNamesSelector)
		{
			foreach (var item in items)
				yield return FileListExportRowFactory.FromListedItem(item, rootPath, tagNamesSelector(item));
		}

		public static IEnumerable<FileListExportRow> EnumerateRecursive(
			IReadOnlyList<FileListExportRow> currentLevelRows,
			string rootPath,
			SortOption sortOption,
			SortDirection sortDirection,
			DirectoryFilterSpec? filterSpec,
			IReadOnlyDictionary<string, IReadOnlyList<string>> tagsByPath,
			FileListExportContext context,
			Func<string, IReadOnlyList<FileSystemInfo>>? listDirectory = null,
			Func<IReadOnlyList<string>, string?>? formatTagNames = null)
		{
			foreach (var row in currentLevelRows)
				yield return row;

			var walker = new RecursiveWalker(rootPath, sortOption, sortDirection, filterSpec, tagsByPath, context, listDirectory, formatTagNames);
			foreach (var row in walker.WalkRootSubfolders())
				yield return row;
		}

		private sealed class RecursiveWalker(
			string rootPath,
			SortOption sortOption,
			SortDirection sortDirection,
			DirectoryFilterSpec? filterSpec,
			IReadOnlyDictionary<string, IReadOnlyList<string>> tagsByPath,
			FileListExportContext context,
			Func<string, IReadOnlyList<FileSystemInfo>>? listDirectory,
			Func<IReadOnlyList<string>, string?>? formatTagNames)
		{
			private readonly Func<string, IReadOnlyList<FileSystemInfo>> listDirectory = listDirectory ?? DefaultListDirectory;

			public IEnumerable<FileListExportRow> WalkRootSubfolders()
			{
				// The root level rows come from the caller (view order); only walk the root's
				// subdirectories, never re-emit root entries from disk.
				foreach (var entry in ListEntries(rootPath))
				{
					if (entry is not DirectoryInfo directory || IsReparsePoint(entry))
						continue;

					foreach (var row in WalkDirectory(directory.FullName))
						yield return row;
				}
			}

			private IEnumerable<FileListExportRow> WalkDirectory(string directoryPath)
			{
				var entries = ListEntries(directoryPath);
				if (entries.Count == 0)
					yield break;

				var rows = BuildRows(entries);

				var sorter = FileListExportSorter.Create(sortOption, sortDirection);
				foreach (var row in rows.OrderBy(row => row, sorter))
					yield return row;

				// Walk every subdirectory on disk, including folders whose own row was
				// filtered out; reparse points are never entered.
				foreach (var entry in entries)
				{
					if (entry is not DirectoryInfo directory || IsReparsePoint(entry))
						continue;

					foreach (var row in WalkDirectory(directory.FullName))
						yield return row;
				}
			}

			private List<FileListExportRow> BuildRows(IReadOnlyList<FileSystemInfo> entries)
			{
				var rows = new List<FileListExportRow>(entries.Count);
				var rowTagIds = new List<IReadOnlyList<string>>(entries.Count);
				foreach (var entry in entries)
				{
					var tagIds = tagsByPath.TryGetValue(entry.FullName, out var ids) ? ids : [];
					try
					{
						rows.Add(FileListExportRowFactory.FromFileSystemInfo(entry, rootPath, FormatTagNames(tagIds)));
						rowTagIds.Add(tagIds);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
						context.AddSkipped();
					}
				}

				if (filterSpec is null || filterSpec.IsEmpty)
					return rows;

				var snapshots = new DirectoryItemFilterSnapshot[rows.Count];
				for (var index = 0; index < rows.Count; index++)
				{
					var row = rows[index];
					snapshots[index] = new DirectoryItemFilterSnapshot(
						index,
						row.Name,
						row.Name,
						row.FullPath,
						row.Extension,
						row.ItemType,
						rowTagIds[index],
						row.SizeBytes,
						row.DateModified,
						row.DateCreated,
						row.IsFolder);
				}

				var matches = DirectoryFilterEvaluator.Evaluate(snapshots, filterSpec);
				var matched = new HashSet<long>(matches);
				return rows.Where((_, index) => matched.Contains(index)).ToList();
			}

			private IReadOnlyList<FileSystemInfo> ListEntries(string directoryPath)
			{
				try
				{
					return listDirectory(directoryPath);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					context.AddSkipped();
					return [];
				}
			}

			private string? FormatTagNames(IReadOnlyList<string>? tagIds)
			{
				if (tagIds is not { Count: > 0 })
					return null;
				return formatTagNames is null ? string.Join("; ", tagIds) : formatTagNames(tagIds);
			}

			private static bool IsReparsePoint(FileSystemInfo entry)
				=> (entry.Attributes & System.IO.FileAttributes.ReparsePoint) != 0;

			private static IReadOnlyList<FileSystemInfo> DefaultListDirectory(string directoryPath)
				=> new List<FileSystemInfo>(new DirectoryInfo(directoryPath).EnumerateFileSystemInfos());
		}
	}
}
