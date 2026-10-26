// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Fixed first-version SQLite schema exposed to the query workbench.
	/// Times are Unix seconds in UTC; unknown values are NULL.
	/// </summary>
	public static class DirectoryQuerySchema
	{
		public const string CreateFilesTable = """
			CREATE TABLE files (
				file_id INTEGER PRIMARY KEY,
				name TEXT,
				display_name TEXT,
				extension TEXT,
				full_path TEXT,
				item_type TEXT,
				size_bytes INTEGER,
				modified_utc INTEGER,
				created_utc INTEGER,
				is_folder INTEGER NOT NULL
			);
			""";

		public const string CreateTagsTable = """
			CREATE TABLE tags (
				tag_id TEXT PRIMARY KEY,
				name TEXT NOT NULL,
				color TEXT
			);
			""";

		public const string CreateFileTagsTable = """
			CREATE TABLE file_tags (
				file_id INTEGER NOT NULL,
				tag_id TEXT NOT NULL,
				PRIMARY KEY (file_id, tag_id)
			);
			""";

		public static IReadOnlyList<string> CreateStatements { get; } =
		[
			CreateFilesTable,
			CreateTagsTable,
			CreateFileTagsTable,
		];
	}
}
