// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class FileListCsvWriterTests
	{
		private static string WriteToString(
			IReadOnlyList<(FileListExportColumn Column, string Header)> columns,
			IEnumerable<FileListExportRow> rows,
			out byte[] bytes)
		{
			using var stream = new MemoryStream();
			FileListCsvWriter.Write(stream, columns, rows);
			bytes = stream.ToArray();
			// Decode after the 3 BOM bytes; the BOM itself is asserted separately.
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		}

		private static FileListExportRow CreateRow(
			string name = "a.txt",
			long? sizeBytes = 5,
			DateTimeOffset? dateModified = null,
			bool isFolder = false,
			bool isHidden = false,
			string? itemType = null,
			string? sizeDisplay = null,
			string? tagNames = null)
			=> new(
				name,
				".txt",
				itemType,
				sizeBytes,
				sizeDisplay,
				dateModified,
				dateModified,
				@"C:\A\" + name,
				@".\" + name,
				isFolder,
				isHidden,
				tagNames);

		[TestMethod]
		public void Write_EmitsBomAndCrlfAndLocalizedHeaders()
		{
			var text = WriteToString(
				[(FileListExportColumn.Name, "名称"), (FileListExportColumn.SizeBytes, "大小(字节)")],
				[CreateRow(sizeBytes: 5)],
				out var bytes);

			Assert.IsTrue(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "output must start with a UTF-8 BOM");
			Assert.AreEqual("名称,大小(字节)\r\na.txt,5\r\n", text);
		}

		[TestMethod]
		public void Write_EscapesSpecialFieldsPerRfc4180()
		{
			var text = WriteToString(
				[(FileListExportColumn.Name, "Name")],
				[
					CreateRow(name: "a,b"),
					CreateRow(name: "say \"hi\""),
					CreateRow(name: "line1\nline2"),
					CreateRow(name: "=cmd"),
				],
				out _);

			var lines = text.Split("\r\n");
			Assert.AreEqual("\"a,b\"", lines[1]);
			Assert.AreEqual("\"say \"\"hi\"\"\"", lines[2]);
			Assert.AreEqual("\"line1\nline2\"", lines[3]);
			Assert.AreEqual("=cmd", lines[4], "formula-looking names pass through unescaped by design");
		}

		[TestMethod]
		public void Write_EmptyValuesStayEmpty()
		{
			var text = WriteToString(
				[(FileListExportColumn.SizeBytes, "Size"), (FileListExportColumn.DateModified, "Modified"), (FileListExportColumn.SizeDisplay, "Display")],
				[
					CreateRow(sizeBytes: null, isFolder: true),
					CreateRow(name: "empty.txt", sizeBytes: 0),
				],
				out _);

			var lines = text.Split("\r\n");
			Assert.AreEqual(",,", lines[1], "folder/null values stay empty");
			Assert.AreEqual("0,,", lines[2], "real zero size exports 0");
		}

		[TestMethod]
		public void Write_BoolColumnsAsLowercaseText()
		{
			var text = WriteToString(
				[(FileListExportColumn.IsFolder, "IsFolder"), (FileListExportColumn.IsHidden, "IsHidden")],
				[
					CreateRow(isFolder: true, isHidden: false),
					CreateRow(isFolder: false, isHidden: true),
				],
				out _);

			var lines = text.Split("\r\n");
			Assert.AreEqual("true,false", lines[1]);
			Assert.AreEqual("false,true", lines[2]);
		}

		[TestMethod]
		public void Write_RendersEveryCatalogColumn()
		{
			var date = new DateTimeOffset(new DateTime(2026, 10, 31, 15, 4, 5));
			var row = CreateRow(name: "r.txt", sizeBytes: 7, dateModified: date, itemType: "Text", sizeDisplay: "7 B", tagNames: "红; 蓝");

			var text = WriteToString(
				FileListExportColumns.All.Select(c => (c, c.ToString())).ToList(),
				[row],
				out _);

			var fields = text.Split("\r\n")[1].Split(',');
			Assert.AreEqual("r.txt", fields[0]);
			Assert.AreEqual(".txt", fields[1]);
			Assert.AreEqual("Text", fields[2]);
			Assert.AreEqual("7", fields[3]);
			Assert.AreEqual("7 B", fields[4]);
			Assert.AreEqual("2026-10-31 15:04:05", fields[5]);
			Assert.AreEqual("2026-10-31 15:04:05", fields[6]);
			Assert.AreEqual(@"C:\A\r.txt", fields[7]);
			Assert.AreEqual(@".\r.txt", fields[8]);
			Assert.AreEqual("false", fields[9]);
			Assert.AreEqual("false", fields[10]);
			Assert.AreEqual("红; 蓝", fields[11]);
		}
	}
}
