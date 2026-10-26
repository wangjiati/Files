// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Globalization;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Decides whether a SQL result can also drive the file list. The raw table is always kept.
	/// </summary>
	public static class DirectoryQueryResultClassifier
	{
		public const string FileIdColumnName = "file_id";

		public static DirectoryQueryResult Classify(DirectoryQueryResult result, DirectoryQuerySnapshot snapshot)
		{
			if (!result.IsSuccess || result.Rows.Count == 0)
				return result;

			var fileIdIndex = FindFileIdColumn(result.Columns);

			if (fileIdIndex < 0)
				return result;

			var knownIds = new HashSet<long>(snapshot.Files.Count);
			foreach (var file in snapshot.Files)
				knownIds.Add(file.FileId);

			var fileIds = new List<long>(result.Rows.Count);
			var seenIds = new HashSet<long>();

			foreach (var row in result.Rows)
			{
				if (fileIdIndex >= row.Values.Count)
					return result;

				if (!TryGetFileId(row.Values[fileIdIndex], out var fileId) || !knownIds.Contains(fileId))
					return result;

				// The file list de-duplicates; the raw table keeps every row.
				if (seenIds.Add(fileId))
					fileIds.Add(fileId);
			}

			return result with
			{
				FileResult = new FileQueryResult(fileIds, result.DirectoryKey, result.SourceRevision),
			};
		}

		private static int FindFileIdColumn(IReadOnlyList<DirectoryQueryColumn> columns)
		{
			for (var index = 0; index < columns.Count; index++)
			{
				if (string.Equals(columns[index].Name, FileIdColumnName, StringComparison.OrdinalIgnoreCase))
					return index;
			}

			return -1;
		}

		private static bool TryGetFileId(object? value, out long fileId)
		{
			switch (value)
			{
				case long longValue:
					fileId = longValue;
					return true;

				case int intValue:
					fileId = intValue;
					return true;

				case short shortValue:
					fileId = shortValue;
					return true;

				case byte byteValue:
					fileId = byteValue;
					return true;

				case ulong ulongValue when ulongValue <= long.MaxValue:
					fileId = (long)ulongValue;
					return true;

				case decimal decimalValue when decimalValue == decimal.Truncate(decimalValue):
					fileId = (long)decimalValue;
					return true;

				case double doubleValue when doubleValue == Math.Truncate(doubleValue):
					fileId = (long)doubleValue;
					return true;

				case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
					fileId = parsed;
					return true;

				default:
					fileId = 0;
					return false;
			}
		}
	}
}
