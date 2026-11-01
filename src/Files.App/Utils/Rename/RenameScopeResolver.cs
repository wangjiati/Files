// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Resolves a batch rename scope into the flat list of rename targets the rules apply to.
	/// </summary>
	/// <remarks>
	/// SelectedItems maps the selection; CurrentFolder reads the live file list of the active shell view
	/// (<see cref="ShellViewModel.FilesAndFolders"/>, minus the ".." entry and the current folder item itself);
	/// CurrentFolderWithSubfolders walks the real folder tree breadth-first with WinRT storage enumeration and
	/// prunes by depth. Every scope honors the file/folder filters; a scope that cannot be anchored (no current
	/// folder path, no active shell view) resolves to an empty list instead of throwing.
	/// </remarks>
	public sealed class RenameScopeResolver : IRenameScopeResolver
	{
		/// <inheritdoc/>
		public async Task<IReadOnlyList<RenameTarget>> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem? currentFolderItem, CancellationToken ct)
		{
			ArgumentNullException.ThrowIfNull(selectedItems);

			if (!includeFiles && !includeFolders)
				return [];

			return option switch
			{
				RenameScopeOption.SelectedItems => ResolveSelectedItems(selectedItems, includeFiles, includeFolders),
				RenameScopeOption.CurrentFolder => ResolveCurrentFolder(currentFolderItem, includeFiles, includeFolders),
				_ => await ResolveWithSubfoldersAsync(currentFolderItem, depth, includeFiles, includeFolders, ct),
			};
		}

		private static IReadOnlyList<RenameTarget> ResolveSelectedItems(IReadOnlyList<ListedItem> selectedItems, bool includeFiles, bool includeFolders)
		{
			List<RenameTarget> targets = new(selectedItems.Count);

			foreach (ListedItem item in selectedItems)
			{
				if (MatchesTypeFilter(item.IsFolder, includeFiles, includeFolders))
					targets.Add(ToTarget(item));
			}

			return targets;
		}

		private static IReadOnlyList<RenameTarget> ResolveCurrentFolder(ListedItem? currentFolderItem, bool includeFiles, bool includeFolders)
		{
			var filesAndFolders = Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage?.ShellViewModel?.FilesAndFolders;
			if (filesAndFolders is null)
				return [];

			List<RenameTarget> targets = [];

			foreach (ListedItem item in filesAndFolders)
			{
				// The ".." entry and the current folder itself are navigation stubs, not rename candidates.
				if (item.ItemNameRaw is ".." || IsCurrentFolderItem(item, currentFolderItem))
					continue;

				if (MatchesTypeFilter(item.IsFolder, includeFiles, includeFolders))
					targets.Add(ToTarget(item));
			}

			return targets;
		}

		private async Task<IReadOnlyList<RenameTarget>> ResolveWithSubfoldersAsync(ListedItem? currentFolderItem, int depth, bool includeFiles, bool includeFolders, CancellationToken ct)
		{
			string? rootPath = currentFolderItem?.ItemPath;
			if (string.IsNullOrEmpty(rootPath))
				return [];

			var rootResult = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFolderFromPathAsync(rootPath));
			if (rootResult.Result is not BaseStorageFolder rootFolder)
				return [];

			List<RenameTarget> targets = [];

			// Breadth-first walk; each entry carries the folder and its path relative to the root ("" for the root).
			Queue<(BaseStorageFolder Folder, string RelativePath)> queue = new();
			queue.Enqueue((rootFolder, string.Empty));

			while (queue.TryDequeue(out var entry))
			{
				ct.ThrowIfCancellationRequested();

				var (folder, folderRelativePath) = entry;
				string parentFolderName = folder.Name;

				if (includeFiles)
				{
					foreach (BaseStorageFile file in await folder.GetFilesAsync().AsTask(ct) ?? [])
					{
						string relativePath = CombineRelative(folderRelativePath, file.Name);
						if (!SubfolderDepth.IsWithinDepth(relativePath, depth))
							continue;

						var props = await file.GetBasicPropertiesAsync();
						targets.Add(new RenameTarget(GetItemPath(file.Path, folder.Path, file.Name), file.Name, false, file.DateCreated, props.DateModified, parentFolderName));
					}
				}

				// Subfolders are needed to include them, and to keep descending while deeper items stay in scope.
				if (includeFolders || depth > 1)
				{
					foreach (BaseStorageFolder subFolder in await folder.GetFoldersAsync().AsTask(ct) ?? [])
					{
						string subRelativePath = CombineRelative(folderRelativePath, subFolder.Name);

						if (includeFolders && SubfolderDepth.IsWithinDepth(subRelativePath, depth))
						{
							var props = await subFolder.GetBasicPropertiesAsync();
							targets.Add(new RenameTarget(GetItemPath(subFolder.Path, folder.Path, subFolder.Name), subFolder.Name, true, subFolder.DateCreated, props.DateModified, parentFolderName));
						}

						// Prune: children of this folder are one level deeper, so only descend while that still fits.
						if (SubfolderDepth.IsWithinDepth(subRelativePath, depth - 1))
							queue.Enqueue((subFolder, subRelativePath));
					}
				}
			}

			return targets;
		}

		private static bool MatchesTypeFilter(bool isFolder, bool includeFiles, bool includeFolders)
		{
			return isFolder ? includeFolders : includeFiles;
		}

		private static bool IsCurrentFolderItem(ListedItem item, ListedItem? currentFolderItem)
		{
			return ReferenceEquals(item, currentFolderItem) ||
				(currentFolderItem?.ItemPath is string path &&
					string.Equals(item.ItemPath, path, StringComparison.OrdinalIgnoreCase));
		}

		private static RenameTarget ToTarget(ListedItem item)
		{
			string fullPath = item.ItemPath ?? string.Empty;

			return new RenameTarget(
				fullPath,
				item.ItemNameRaw ?? string.Empty,
				item.IsFolder,
				item.ItemDateCreatedReal,
				item.ItemDateModifiedReal,
				Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty);
		}

		private static string CombineRelative(string folderRelativePath, string name)
		{
			return folderRelativePath.Length == 0 ? name : folderRelativePath + "\\" + name;
		}

		// Virtual folders (MTP etc.) report empty child paths; rebuild from the parent like the file list does.
		private static string GetItemPath(string? childPath, string? parentPath, string name)
		{
			return string.IsNullOrEmpty(childPath) ? Path.Combine(parentPath ?? string.Empty, name) : childPath;
		}
	}
}
