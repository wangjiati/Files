// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Helpers;
using System;
using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Sorts export rows the way the folder view is sorted: folders always first,
	/// then by the current sort option; options with no row equivalent fall back to name.
	/// </summary>
	public static class FileListExportSorter
	{
		public static IComparer<FileListExportRow> Create(SortOption option, SortDirection direction)
		{
			var ascending = direction == SortDirection.Ascending;
			var nameComparer = NaturalStringComparer.GetForProcessor();

			return Comparer<FileListExportRow>.Create((x, y) =>
			{
				if (x.IsFolder != y.IsFolder)
					return x.IsFolder ? -1 : 1;

				var result = option switch
				{
					SortOption.Size => CompareNullable(x.SizeBytes, y.SizeBytes),
					SortOption.DateModified => CompareNullable(x.DateModified, y.DateModified),
					SortOption.DateCreated => CompareNullable(x.DateCreated, y.DateCreated),
					SortOption.FileType => string.Compare(x.ItemType ?? x.Extension ?? string.Empty, y.ItemType ?? y.Extension ?? string.Empty, StringComparison.OrdinalIgnoreCase),
					SortOption.Path => string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase),
					_ => nameComparer.Compare(x.Name, y.Name),
				};

				return ascending ? result : -result;
			});
		}

		private static int CompareNullable<T>(T? x, T? y)
			where T : struct, IComparable<T>
		{
			if (x is null)
				return y is null ? 0 : 1;
			if (y is null)
				return -1;
			return x.Value.CompareTo(y.Value);
		}
	}
}
