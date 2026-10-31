// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// One exported row. Raw values only: unknown size/dates stay null so consumers never
	/// mistake them for zero or the smallest representable value.
	/// </summary>
	public sealed record FileListExportRow(
		string Name,
		string? Extension,
		string? ItemType,
		long? SizeBytes,
		string? SizeDisplay,
		DateTimeOffset? DateCreated,
		DateTimeOffset? DateModified,
		string FullPath,
		string RelativePath,
		bool IsFolder,
		bool IsHidden,
		string? TagNames);
}
