// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Transient group, sort and layout values carried by a view address. These are
	/// applied on top of the folder's saved preferences and are never persisted.
	/// </summary>
	public sealed record DirectoryViewOverrides
	{
		public static DirectoryViewOverrides Empty { get; } = new();

		public GroupOption? GroupOption { get; init; }

		public SortDirection? GroupDirection { get; init; }

		public GroupByDateUnit? GroupByDateUnit { get; init; }

		public SortOption? SortOption { get; init; }

		public SortDirection? SortDirection { get; init; }

		public FolderLayoutModes? LayoutMode { get; init; }

		public bool IsEmpty =>
			GroupOption is null &&
			GroupDirection is null &&
			GroupByDateUnit is null &&
			SortOption is null &&
			SortDirection is null &&
			LayoutMode is null;
	}
}
