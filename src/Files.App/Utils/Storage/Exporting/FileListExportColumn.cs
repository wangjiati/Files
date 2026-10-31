// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Columns available for file list export. Order defines the catalog order shown in the dialog.
	/// </summary>
	public enum FileListExportColumn : ushort
	{
		Name,
		Extension,
		ItemType,
		SizeBytes,
		SizeDisplay,
		DateCreated,
		DateModified,
		FullPath,
		RelativePath,
		IsFolder,
		IsHidden,
		FileTags,
	}

	public static class FileListExportColumns
	{
		/// <summary>Every exportable column in catalog order.</summary>
		public static readonly IReadOnlyList<FileListExportColumn> All =
		[
			FileListExportColumn.Name,
			FileListExportColumn.Extension,
			FileListExportColumn.ItemType,
			FileListExportColumn.SizeBytes,
			FileListExportColumn.SizeDisplay,
			FileListExportColumn.DateCreated,
			FileListExportColumn.DateModified,
			FileListExportColumn.FullPath,
			FileListExportColumn.RelativePath,
			FileListExportColumn.IsFolder,
			FileListExportColumn.IsHidden,
			FileListExportColumn.FileTags,
		];

		/// <summary>Columns checked by default: name, item type, size in bytes, date modified, full path.</summary>
		public static IReadOnlyList<FileListExportColumn> DefaultSelection =>
		[
			FileListExportColumn.Name,
			FileListExportColumn.ItemType,
			FileListExportColumn.SizeBytes,
			FileListExportColumn.DateModified,
			FileListExportColumn.FullPath,
		];

		public static string GetHeaderResourceKey(FileListExportColumn column)
			=> "ExportFileList_Column" + column switch
			{
				FileListExportColumn.Name => nameof(FileListExportColumn.Name),
				FileListExportColumn.Extension => nameof(FileListExportColumn.Extension),
				FileListExportColumn.ItemType => nameof(FileListExportColumn.ItemType),
				FileListExportColumn.SizeBytes => nameof(FileListExportColumn.SizeBytes),
				FileListExportColumn.SizeDisplay => nameof(FileListExportColumn.SizeDisplay),
				FileListExportColumn.DateCreated => nameof(FileListExportColumn.DateCreated),
				FileListExportColumn.DateModified => nameof(FileListExportColumn.DateModified),
				FileListExportColumn.FullPath => nameof(FileListExportColumn.FullPath),
				FileListExportColumn.RelativePath => nameof(FileListExportColumn.RelativePath),
				FileListExportColumn.IsFolder => nameof(FileListExportColumn.IsFolder),
				FileListExportColumn.IsHidden => nameof(FileListExportColumn.IsHidden),
				FileListExportColumn.FileTags => nameof(FileListExportColumn.FileTags),
				_ => column.ToString(),
			};
	}
}
