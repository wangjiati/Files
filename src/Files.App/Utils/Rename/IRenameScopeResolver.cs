// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Resolves the batch rename scope into the flat list of items the rename rules apply to.
	/// </summary>
	public interface IRenameScopeResolver
	{
		/// <summary>
		/// Resolves the requested scope into rename targets, honoring the file/folder filters, the subfolder depth and the cancellation.
		/// </summary>
		/// <param name="option">Which scope to resolve.</param>
		/// <param name="depth">Subfolder depth for <see cref="RenameScopeOption.CurrentFolderWithSubfolders"/>; "a\b" is level 2.</param>
		/// <param name="includeFiles">Whether files participate in the batch.</param>
		/// <param name="includeFolders">Whether folders participate in the batch.</param>
		/// <param name="selectedItems">The selected items of the current file list, used by <see cref="RenameScopeOption.SelectedItems"/>.</param>
		/// <param name="currentFolderItem">The current folder item; anchors <see cref="RenameScopeOption.CurrentFolder"/> and <see cref="RenameScopeOption.CurrentFolderWithSubfolders"/>.</param>
		/// <param name="ct">Cancels the subfolder enumeration.</param>
		Task<IReadOnlyList<RenameTarget>> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem currentFolderItem, CancellationToken ct);
	}
}
