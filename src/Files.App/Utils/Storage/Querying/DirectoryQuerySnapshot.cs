// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Scalar copy of one listed item for the read-only SQL snapshot.
	/// Unknown sizes and dates stay null so SQL never sees a fabricated numeric value.
	/// </summary>
	public sealed record DirectoryQueryFileSnapshot(
		long FileId,
		string? Name,
		string? DisplayName,
		string? Extension,
		string? FullPath,
		string? ItemType,
		long? SizeBytes,
		DateTimeOffset? ModifiedUtc,
		DateTimeOffset? CreatedUtc,
		bool IsFolder);

	public sealed record DirectoryQueryTagSnapshot(string TagId, string Name, string? Color);

	public sealed record DirectoryQueryFileTagSnapshot(long FileId, string TagId);

	/// <summary>
	/// Immutable snapshot that one explicit SQL execution is evaluated against.
	/// </summary>
	public sealed record DirectoryQuerySnapshot(
		string DirectoryKey,
		long SourceRevision,
		IReadOnlyList<DirectoryQueryFileSnapshot> Files,
		IReadOnlyList<DirectoryQueryTagSnapshot> Tags,
		IReadOnlyList<DirectoryQueryFileTagSnapshot> FileTags);
}
