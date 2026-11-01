// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// The outcome of a scope resolution: the rename targets plus the names already present in every
	/// folder the targets live in.
	/// </summary>
	/// <remarks>
	/// <see cref="ExistingNamesInFolders"/> seeds conflict detection with real sibling names, so a
	/// proposed name that lands on an item outside the batch conflicts too; folders without an entry
	/// fall back to the batch's own original names.
	/// </remarks>
	public sealed record RenameScopeResult(
		IReadOnlyList<RenameTarget> Targets,
		IReadOnlyDictionary<string, IReadOnlyList<string>> ExistingNamesInFolders)
	{
		public static RenameScopeResult Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>());
	}

	/// <summary>
	/// Resolves the batch rename scope into the flat list of items the rename rules apply to.
	/// </summary>
	public interface IRenameScopeResolver
	{
		/// <summary>
		/// Resolves the requested scope into rename targets and their folders' existing names, honoring the file/folder filters, the subfolder depth and the cancellation.
		/// </summary>
		/// <param name="option">Which scope to resolve.</param>
		/// <param name="depth">Subfolder depth for <see cref="RenameScopeOption.CurrentFolderWithSubfolders"/>; "a\b" is level 2.</param>
		/// <param name="includeFiles">Whether files participate in the batch.</param>
		/// <param name="includeFolders">Whether folders participate in the batch.</param>
		/// <param name="selectedItems">The selected items of the current file list, used by <see cref="RenameScopeOption.SelectedItems"/>.</param>
		/// <param name="currentFolderItem">The current folder item; anchors <see cref="RenameScopeOption.CurrentFolder"/> and <see cref="RenameScopeOption.CurrentFolderWithSubfolders"/>; <c>null</c> leaves those scopes unanchored.</param>
		/// <param name="ct">Cancels the subfolder enumeration.</param>
		Task<RenameScopeResult> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem? currentFolderItem, CancellationToken ct);
	}
}
