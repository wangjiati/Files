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

				// Unknown values sort last regardless of direction, matching folder view behavior.
				if (GetNullOrder(option, x, y) is { } nullOrder && nullOrder != 0)
					return nullOrder;

				var result = option switch
				{
					SortOption.Size => x.SizeBytes!.Value.CompareTo(y.SizeBytes!.Value),
					SortOption.DateModified => x.DateModified!.Value.CompareTo(y.DateModified!.Value),
					SortOption.DateCreated => x.DateCreated!.Value.CompareTo(y.DateCreated!.Value),
					SortOption.FileType => string.Compare(x.ItemType ?? x.Extension ?? string.Empty, y.ItemType ?? y.Extension ?? string.Empty, StringComparison.OrdinalIgnoreCase),
					SortOption.Path => string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase),
					_ => nameComparer.Compare(x.Name, y.Name),
				};

				return ascending ? result : -result;
			});
		}

		private static int? GetNullOrder(SortOption option, FileListExportRow x, FileListExportRow y)
			=> option switch
			{
				SortOption.Size => NullOrder(x.SizeBytes.HasValue, y.SizeBytes.HasValue),
				SortOption.DateModified => NullOrder(x.DateModified.HasValue, y.DateModified.HasValue),
				SortOption.DateCreated => NullOrder(x.DateCreated.HasValue, y.DateCreated.HasValue),
				_ => null,
			};

		private static int NullOrder(bool xHasValue, bool yHasValue)
			=> (xHasValue, yHasValue) switch
			{
				(true, true) => 0,
				(false, true) => 1,
				(true, false) => -1,
				_ => 0,
			};
	}
}
