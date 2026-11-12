// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;

namespace Files.App.Utils.Storage
{
	public static class SortingHelper
	{
		private static object? OrderByNameFunc(ListedItem item)
			=> item.Name;

		public static Func<ListedItem, object?> GetSortFunc(SortOption directorySortOption)
		{
			return directorySortOption switch
			{
				SortOption.Name => item => item.Name,
				SortOption.DateModified => item => item.ItemDateModifiedReal,
				SortOption.DateCreated => item => item.ItemDateCreatedReal,
				SortOption.FileType => item => item.ItemType,
				SortOption.Size => item => item.FileSizeBytes,
				SortOption.SyncStatus => item => item.SyncStatusString,
				SortOption.FileTag => item => item.FileTags?.FirstOrDefault(),
				SortOption.Path => item => item.ItemPath,
				SortOption.OriginalFolder => item => (item as RecycleBinItem)?.ItemOriginalFolder,
				SortOption.DateDeleted => item => (item as RecycleBinItem)?.ItemDateDeletedReal,
				_ => item => item.Name,
			};
		}

		/// <summary>
		/// Builds a comparison equivalent to the ordering produced by <see cref="OrderFileList"/> with the
		/// same options, including the folder/file priority clause, the file-tag empty-last clause and the
		/// natural-name tiebreak. Used to place single items into an already ordered list.
		/// </summary>
		public static Comparison<ListedItem> GetFileListComparison(SortOption directorySortOption, SortDirection directorySortDirection,
			bool sortDirectoriesAlongsideFiles, bool sortFilesFirst)
		{
			var orderFunc = GetSortFunc(directorySortOption);
			var naturalStringComparer = NaturalStringComparer.GetForProcessor();
			var descending = directorySortDirection == SortDirection.Descending;

			return (x, y) =>
			{
				if (!sortDirectoriesAlongsideFiles)
				{
					var priority = Prioritize(x).CompareTo(Prioritize(y));
					if (priority != 0)
						return priority;
				}

				// FileTag sorting keeps untagged items last regardless of the direction
				if (directorySortOption == SortOption.FileTag)
				{
					var emptyLast = string.IsNullOrEmpty(orderFunc(x) as string).CompareTo(string.IsNullOrEmpty(orderFunc(y) as string));
					if (emptyLast != 0)
						return emptyLast;
				}

				var result = directorySortOption == SortOption.Name
					? naturalStringComparer.Compare(orderFunc(x), orderFunc(y))
					: CompareSortKeys(orderFunc(x), orderFunc(y));
				if (result != 0)
					return descending ? -result : result;

				if (directorySortOption == SortOption.Name)
					return 0;

				result = naturalStringComparer.Compare(OrderByNameFunc(x), OrderByNameFunc(y));
				return descending ? -result : result;
			};

			// Mirrors PrioritizeFilesOrFolders in OrderFileList
			bool Prioritize(ListedItem item)
				=> (item.PrimaryItemAttribute == StorageItemTypes.File || item.IsShortcut || item.IsArchive) ^ sortFilesFirst;
		}

		// Mirrors OrderBy(orderFunc): Comparer<object>.Default orders nulls first, then by IComparable
		private static int CompareSortKeys(object? x, object? y)
		{
			if (x is null && y is null)
				return 0;
			if (x is null)
				return -1;
			if (y is null)
				return 1;
			return ((IComparable)x).CompareTo(y);
		}

		public static IEnumerable<ListedItem> OrderFileList(IList<ListedItem> filesAndFolders, SortOption directorySortOption, SortDirection directorySortDirection,
			bool sortDirectoriesAlongsideFiles, bool sortFilesFirst)
		{
			var orderFunc = GetSortFunc(directorySortOption);
			var naturalStringComparer = NaturalStringComparer.GetForProcessor();

			// Function to prioritize folders (if sortFilesFirst is false) or files (if sortFilesFirst is true)
			bool PrioritizeFilesOrFolders(ListedItem listedItem)
				=> (listedItem.PrimaryItemAttribute == StorageItemTypes.File || listedItem.IsShortcut || listedItem.IsArchive) ^ sortFilesFirst;

			IOrderedEnumerable<ListedItem> ordered;

			if (directorySortDirection == SortDirection.Ascending)
			{
				ordered = directorySortOption switch
				{
					SortOption.Name => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderBy(orderFunc, naturalStringComparer)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders).ThenBy(orderFunc, naturalStringComparer),

					SortOption.FileTag => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderBy(x => string.IsNullOrEmpty(orderFunc(x) as string)).ThenBy(orderFunc)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders)
							.ThenBy(x => string.IsNullOrEmpty(orderFunc(x) as string))
							.ThenBy(orderFunc),

					_ => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderBy(orderFunc)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders).ThenBy(orderFunc)
				};
			}
			else
			{
				ordered = directorySortOption switch
				{
					SortOption.Name => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderByDescending(orderFunc, naturalStringComparer)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders)
							.ThenByDescending(orderFunc, naturalStringComparer),

					SortOption.FileTag => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderBy(x => string.IsNullOrEmpty(orderFunc(x) as string))
							.ThenByDescending(orderFunc)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders)
							.ThenBy(x => string.IsNullOrEmpty(orderFunc(x) as string))
							.ThenByDescending(orderFunc),

					_ => sortDirectoriesAlongsideFiles
						? filesAndFolders.OrderByDescending(orderFunc)
						: filesAndFolders.OrderBy(PrioritizeFilesOrFolders).ThenByDescending(orderFunc)
				};
			}

			// Further order by name if applicable
			if (directorySortOption != SortOption.Name)
			{
				ordered = directorySortDirection == SortDirection.Ascending
					? ordered.ThenBy(OrderByNameFunc, naturalStringComparer)
					: ordered.ThenByDescending(OrderByNameFunc, naturalStringComparer);
			}

			return ordered;
		}
	}
}
