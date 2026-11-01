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
	/// prunes by depth. Every scope also reports the names present in each involved folder, from the
	/// live listing for the shell-anchored scopes and from the enumeration itself for the subfolder
	/// walk, so conflict detection sees items outside the batch. A scope that cannot be anchored (no
	/// current folder path, no active shell view) resolves to an empty list instead of throwing; a
	/// subfolder walk whose root cannot be opened fails like any of its child enumerations.
	/// </remarks>
	public sealed class RenameScopeResolver : IRenameScopeResolver
	{
		/// <inheritdoc/>
		public async Task<RenameScopeResult> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem? currentFolderItem, CancellationToken ct)
		{
			ArgumentNullException.ThrowIfNull(selectedItems);

			if (!includeFiles && !includeFolders)
				return RenameScopeResult.Empty;

			return option switch
			{
				RenameScopeOption.SelectedItems => ResolveSelectedItems(selectedItems, includeFiles, currentFolderItem),
				RenameScopeOption.CurrentFolder => ResolveCurrentFolder(currentFolderItem, includeFiles, includeFolders),
				_ => await ResolveWithSubfoldersAsync(currentFolderItem, depth, includeFiles, includeFolders, ct),
			};
		}

		private static RenameScopeResult ResolveSelectedItems(IReadOnlyList<ListedItem> selectedItems, bool includeFiles, ListedItem? currentFolderItem)
		{
			List<RenameTarget> targets = new(selectedItems.Count);

			foreach (ListedItem item in selectedItems)
			{
				// An explicitly selected folder stays in the batch even while folders are excluded: the
				// preview keeps it as a greyed-out row (ExcludedFolderType) and drops it from the plan.
				if (!item.IsFolder && !includeFiles)
					continue;

				targets.Add(ToTarget(item));
			}

			return new RenameScopeResult(targets, CollectLiveFolderNames(currentFolderItem));
		}

		private static RenameScopeResult ResolveCurrentFolder(ListedItem? currentFolderItem, bool includeFiles, bool includeFolders)
		{
			var filesAndFolders = Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage?.ShellViewModel?.FilesAndFolders;
			if (filesAndFolders is null)
				return RenameScopeResult.Empty;

			List<RenameTarget> targets = [];
			List<string> existingNames = new(filesAndFolders.Count);

			foreach (ListedItem item in filesAndFolders)
			{
				// The ".." entry and the current folder itself are navigation stubs, not rename candidates.
				if (item.ItemNameRaw is ".." || IsCurrentFolderItem(item, currentFolderItem))
					continue;

				// Every live child occupies its name, even when it is filtered out of the targets.
				existingNames.Add(item.ItemNameRaw ?? string.Empty);

				if (MatchesTypeFilter(item.IsFolder, includeFiles, includeFolders))
					targets.Add(ToTarget(item));
			}

			return new RenameScopeResult(targets, ToFolderNames(currentFolderItem, existingNames));
		}

		private async Task<RenameScopeResult> ResolveWithSubfoldersAsync(ListedItem? currentFolderItem, int depth, bool includeFiles, bool includeFolders, CancellationToken ct)
		{
			string? rootPath = currentFolderItem?.ItemPath;
			if (string.IsNullOrEmpty(rootPath))
				return RenameScopeResult.Empty;

			var rootResult = await FilesystemTasks.WrapNullable(() => StorageFileExtensions.DangerousGetFolderFromPathAsync(rootPath));
			if (rootResult.Result is not BaseStorageFolder rootFolder)
				throw new IOException($"The root folder could not be opened: {rootPath}");

			List<RenameTarget> targets = [];
			Dictionary<string, IReadOnlyList<string>> existingNames = new(StringComparer.OrdinalIgnoreCase);

			// Breadth-first walk; each entry carries the folder and its path relative to the root ("" for the root).
			Queue<(BaseStorageFolder Folder, string RelativePath)> queue = new();
			queue.Enqueue((rootFolder, string.Empty));

			while (queue.TryDequeue(out var entry))
			{
				ct.ThrowIfCancellationRequested();

				var (folder, folderRelativePath) = entry;
				string parentFolderName = folder.Name;

				// Both child kinds are always enumerated: children filtered out of the targets still
				// occupy their names for conflict detection.
				IReadOnlyList<BaseStorageFile> files = await folder.GetFilesAsync().AsTask(ct) ?? [];
				IReadOnlyList<BaseStorageFolder> subFolders = await folder.GetFoldersAsync().AsTask(ct) ?? [];
				List<string> folderExistingNames = new(files.Count + subFolders.Count);

				foreach (BaseStorageFile file in files)
				{
					folderExistingNames.Add(file.Name);

					if (!includeFiles)
						continue;

					string relativePath = CombineRelative(folderRelativePath, file.Name);
					if (!SubfolderDepth.IsWithinDepth(relativePath, depth))
						continue;

					var props = await file.GetBasicPropertiesAsync();
					targets.Add(new RenameTarget(GetItemPath(file.Path, folder.Path, file.Name), file.Name, false, file.DateCreated, props.DateModified, parentFolderName));
				}

				foreach (BaseStorageFolder subFolder in subFolders)
				{
					folderExistingNames.Add(subFolder.Name);

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

				existingNames[folder.Path] = folderExistingNames;
			}

			return new RenameScopeResult(targets, existingNames);
		}

		// Names of the live listing of the current folder, keyed by its path; empty when no view is available.
		private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectLiveFolderNames(ListedItem? currentFolderItem)
		{
			var filesAndFolders = Ioc.Default.GetRequiredService<IContentPageContext>().ShellPage?.ShellViewModel?.FilesAndFolders;
			if (filesAndFolders is null)
				return new Dictionary<string, IReadOnlyList<string>>();

			List<string> names = new(filesAndFolders.Count);
			foreach (ListedItem item in filesAndFolders)
			{
				if (item.ItemNameRaw is ".." || IsCurrentFolderItem(item, currentFolderItem))
					continue;

				names.Add(item.ItemNameRaw ?? string.Empty);
			}

			return ToFolderNames(currentFolderItem, names);
		}

		private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToFolderNames(ListedItem? currentFolderItem, List<string> names)
		{
			return currentFolderItem?.ItemPath is string folderPath && folderPath.Length > 0
				? new Dictionary<string, IReadOnlyList<string>> { [folderPath] = names }
				: new Dictionary<string, IReadOnlyList<string>>();
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
