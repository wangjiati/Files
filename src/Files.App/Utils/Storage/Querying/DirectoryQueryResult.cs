// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	public sealed record DirectoryQueryColumn(string Name, string? DeclaredType);

	public sealed record DirectoryQueryRow(IReadOnlyList<object?> Values);

	public enum DirectoryQueryError
	{
		None,
		InvalidSql,
		MultipleStatements,
		NotSelect,
		ForbiddenOperation,
		Timeout,
		Cancelled,
		SqliteError,
	}

	/// <summary>
	/// File view derived from a result whose every row maps to a snapshot file.
	/// </summary>
	public sealed record FileQueryResult(IReadOnlyList<long> FileIds, string DirectoryKey, long SourceRevision);

	public sealed record DirectoryQueryResult(
		IReadOnlyList<DirectoryQueryColumn> Columns,
		IReadOnlyList<DirectoryQueryRow> Rows,
		bool IsTruncated,
		TimeSpan Elapsed,
		string DirectoryKey,
		long SourceRevision,
		DirectoryQueryError Error,
		string? ErrorMessage)
	{
		public bool IsSuccess => Error is DirectoryQueryError.None;

		/// <summary>
		/// Set by <see cref="DirectoryQueryResultClassifier"/> when the result can drive the file list.
		/// </summary>
		public FileQueryResult? FileResult { get; init; }
	}

	public sealed record DirectoryQueryExecutionOptions(
		TimeSpan Timeout,
		int MaxRows,
		int ProgressInterval)
	{
		public static DirectoryQueryExecutionOptions Default { get; } = new(
			TimeSpan.FromSeconds(10),
			MaxRows: 50_000,
			ProgressInterval: 1_000);
	}
}
