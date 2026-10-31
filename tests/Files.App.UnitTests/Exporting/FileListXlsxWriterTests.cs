// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class FileListXlsxWriterTests
	{
		private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

		private static byte[] WritePackage(
			IReadOnlyList<(FileListExportColumn Column, string Header)> columns,
			FileListExportRow[] rows,
			string sheetName = "FileList")
		{
			using var stream = new MemoryStream();
			FileListXlsxWriter.Write(stream, sheetName, columns, rows);
			return stream.ToArray();
		}

		private static Dictionary<string, string> ReadEntries(byte[] package)
		{
			using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
			return zip.Entries.ToDictionary(
				entry => entry.FullName,
				entry =>
				{
					using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
					return reader.ReadToEnd();
				});
		}

		private static FileListExportRow CreateRow(string name = "a.txt", long? sizeBytes = 5)
			=> new(
				name,
				".txt",
				"Text",
				sizeBytes,
				sizeBytes?.ToString(),
				null,
				null,
				@"C:\A\" + name,
				@".\" + name,
				false,
				false,
				null);

		[TestMethod]
		public void Write_ProducesReopenablePackageWithRequiredParts()
		{
			var entries = ReadEntries(WritePackage([(FileListExportColumn.Name, "Name")], [CreateRow()]));

			foreach (var part in new[]
			{
				"[Content_Types].xml",
				"_rels/.rels",
				"xl/workbook.xml",
				"xl/_rels/workbook.xml.rels",
				"xl/worksheets/sheet1.xml",
				"xl/styles.xml",
			})
			{
				Assert.IsTrue(entries.ContainsKey(part), $"package must contain {part}");
				XDocument.Parse(entries[part], XmlLoadOptions);
			}

			var workbook = XDocument.Parse(entries["xl/workbook.xml"], XmlLoadOptions);
			var sheet = workbook.Root!.Element(Ns + "sheets")!.Elements(Ns + "sheet").Single();
			Assert.AreEqual("FileList", (string?)sheet.Attribute("name"));
		}

		[TestMethod]
		public void Write_HeaderRowUsesBoldStyle_AndFieldsAreEscaped()
		{
			var entries = ReadEntries(WritePackage(
				[(FileListExportColumn.Name, "Name"), (FileListExportColumn.FullPath, "Path")],
				[CreateRow(name: "a<b>&c")]));

			var sheet = XDocument.Parse(entries["xl/worksheets/sheet1.xml"], XmlLoadOptions);
			var rows = sheet.Root!.Element(Ns + "sheetData")!.Elements(Ns + "row").ToList();

			var headerCells = rows[0].Elements(Ns + "c").ToList();
			Assert.AreEqual(2, headerCells.Count);
			Assert.AreEqual("1", (string?)headerCells[0].Attribute("s"), "header cells use the bold style xf");
			Assert.AreEqual("1", (string?)headerCells[1].Attribute("s"));
			Assert.AreEqual("inlineStr", (string?)headerCells[0].Attribute("t"));
			Assert.AreEqual("Name", headerCells[0].Element(Ns + "is")!.Element(Ns + "t")!.Value);

			var dataCell = rows[1].Elements(Ns + "c").First();
			Assert.AreEqual("inlineStr", (string?)dataCell.Attribute("t"));
			Assert.AreEqual("a<b>&c", dataCell.Element(Ns + "is")!.Element(Ns + "t")!.Value, "XML-special characters round-trip");
		}

		[TestMethod]
		public void Write_NumericBoolAndDateCells()
		{
			var date = new DateTimeOffset(new DateTime(2026, 10, 31, 15, 4, 5));
			var rows = new[]
			{
				new FileListExportRow("f.txt", ".txt", null, 3, null, null, date, @"C:\A\f.txt", @".\f.txt", false, false, null),
				new FileListExportRow("dir", null, null, null, null, null, null, @"C:\A\dir", @".\dir", true, true, null),
				new FileListExportRow("empty.txt", ".txt", null, 0, null, null, null, @"C:\A\empty.txt", @".\empty.txt", false, false, null),
			};

			var entries = ReadEntries(WritePackage(
				[(FileListExportColumn.SizeBytes, "Size"), (FileListExportColumn.DateModified, "Modified"), (FileListExportColumn.IsFolder, "IsFolder"), (FileListExportColumn.IsHidden, "IsHidden")],
				rows));

			var sheet = XDocument.Parse(entries["xl/worksheets/sheet1.xml"], XmlLoadOptions);
			var dataRows = sheet.Root!.Element(Ns + "sheetData")!.Elements(Ns + "row").Skip(1).ToList();

			var sizeCells = dataRows.Select(r => r.Elements(Ns + "c").First()).ToList();
			Assert.IsNull((string?)sizeCells[0].Attribute("t"), "numeric cells carry no t attribute");
			Assert.AreEqual("3", sizeCells[0].Element(Ns + "v")!.Value);
			Assert.IsFalse(sizeCells[1].Elements().Any(), "null size is an empty cell");
			Assert.AreEqual("0", sizeCells[2].Element(Ns + "v")!.Value, "real zero size exports numeric 0");

			var dateCell = dataRows[0].Elements(Ns + "c").ElementAt(1);
			Assert.AreEqual("2026-10-31 15:04:05", dateCell.Element(Ns + "is")!.Element(Ns + "t")!.Value);

			Assert.AreEqual("true", dataRows[1].Elements(Ns + "c").ElementAt(2).Element(Ns + "is")!.Element(Ns + "t")!.Value);
			Assert.AreEqual("true", dataRows[1].Elements(Ns + "c").ElementAt(3).Element(Ns + "is")!.Element(Ns + "t")!.Value);
		}

		[TestMethod]
		public void Write_StylesFollowMainstreamMinimalStructure()
		{
			var entries = ReadEntries(WritePackage([(FileListExportColumn.Name, "Name")], [CreateRow()]));

			var styles = XDocument.Parse(entries["xl/styles.xml"], XmlLoadOptions).Root!;

			var fonts = styles.Element(Ns + "fonts")!.Elements(Ns + "font").ToList();
			var firstFontChildren = fonts[0].Elements().Select(e => (e.Name.LocalName, (string?)e.Attribute("val"))).ToList();
			CollectionAssert.Contains(firstFontChildren, ("sz", "11"), "default font must declare a size");
			CollectionAssert.Contains(firstFontChildren, ("name", "Calibri"), "default font must declare a family");

			var fills = styles.Element(Ns + "fills")!.Elements(Ns + "fill").ToList();
			Assert.AreEqual(2, fills.Count, "Excel expects the two reserved fills");
			Assert.AreEqual("none", (string?)fills[0].Element(Ns + "patternFill")!.Attribute("patternType"));
			Assert.AreEqual("gray125", (string?)fills[1].Element(Ns + "patternFill")!.Attribute("patternType"));
		}

		[TestMethod]
		public void Write_ThrowsWhenExceedingExcelRowLimit()
		{
			const int excelMaxRows = 1_048_576; // including the header row
			var rows = Enumerable.Range(0, excelMaxRows)
				.Select(i => CreateRow(name: $"r{i}.txt"));

			using var stream = new MemoryStream();
			try
			{
				FileListXlsxWriter.Write(stream, "FileList", [(FileListExportColumn.Name, "Name")], rows);
				Assert.Fail("exporting more rows than Excel supports must fail instead of producing a broken file");
			}
			catch (InvalidOperationException)
			{
			}
		}

		private static LoadOptions XmlLoadOptions => LoadOptions.None;
	}
}
