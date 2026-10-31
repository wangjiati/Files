// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Text;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Writes export rows as UTF-8 (BOM) CSV with RFC 4180 quoting and CRLF line endings,
	/// streaming rows one at a time.
	/// </summary>
	public static class FileListCsvWriter
	{
		public static void Write(Stream stream, IReadOnlyList<(FileListExportColumn Column, string Header)> columns, IEnumerable<FileListExportRow> rows)
		{
			using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

			WriteLine(writer, columns.Select(column => column.Header));
			foreach (var row in rows)
				WriteLine(writer, columns.Select(column => GetCellText(column.Column, row)));
		}

		/// <summary>Text representation of a cell; shared by the CSV and xlsx writers.</summary>
		public static string GetCellText(FileListExportColumn column, FileListExportRow row)
			=> column switch
			{
				FileListExportColumn.Name => row.Name,
				FileListExportColumn.Extension => row.Extension ?? string.Empty,
				FileListExportColumn.ItemType => row.ItemType ?? string.Empty,
				FileListExportColumn.SizeBytes => row.SizeBytes?.ToString() ?? string.Empty,
				FileListExportColumn.SizeDisplay => row.SizeDisplay ?? string.Empty,
				FileListExportColumn.DateCreated => FileListExportRowFactory.FormatDate(row.DateCreated),
				FileListExportColumn.DateModified => FileListExportRowFactory.FormatDate(row.DateModified),
				FileListExportColumn.FullPath => row.FullPath,
				FileListExportColumn.RelativePath => row.RelativePath,
				FileListExportColumn.IsFolder => row.IsFolder ? "true" : "false",
				FileListExportColumn.IsHidden => row.IsHidden ? "true" : "false",
				FileListExportColumn.FileTags => row.TagNames ?? string.Empty,
				_ => string.Empty,
			};

		private static void WriteLine(TextWriter writer, IEnumerable<string> fields)
		{
			writer.Write(string.Join(",", fields.Select(Escape)));
			writer.Write("\r\n");
		}

		private static string Escape(string field)
			=> field.Contains(',') || field.Contains('"') || field.Contains('\r') || field.Contains('\n')
				? "\"" + field.Replace("\"", "\"\"") + "\""
				: field;
	}
}
