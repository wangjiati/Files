// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Minimal single-sheet xlsx writer using ZipArchive and hand-built OpenXML:
	/// inline strings, one bold header style, no column widths. Rows stream into
	/// the zip entry without buffering the whole document.
	/// </summary>
	public static class FileListXlsxWriter
	{
		private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
		private const string RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
		private const string PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

		public static void Write(Stream stream, string sheetName, IReadOnlyList<(FileListExportColumn Column, string Header)> columns, IEnumerable<FileListExportRow> rows)
		{
			using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

			WriteContentTypes(archive);
			WritePackageRelationships(archive);
			WriteWorkbook(archive, sheetName);
			WriteWorkbookRelationships(archive);
			WriteStyles(archive);
			WriteWorksheet(archive, columns, rows);
		}

		private static void WriteContentTypes(ZipArchive archive)
		{
			using var writer = CreateEntryWriter(archive, "[Content_Types].xml");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
			writer.WriteStartElement("Default");
			writer.WriteAttributeString("Extension", "rels");
			writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
			writer.WriteEndElement();
			writer.WriteStartElement("Default");
			writer.WriteAttributeString("Extension", "xml");
			writer.WriteAttributeString("ContentType", "application/xml");
			writer.WriteEndElement();
			WriteOverride(writer, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
			WriteOverride(writer, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
			WriteOverride(writer, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
			writer.WriteEndElement();
		}

		private static void WriteOverride(XmlWriter writer, string partName, string contentType)
		{
			writer.WriteStartElement("Override");
			writer.WriteAttributeString("PartName", partName);
			writer.WriteAttributeString("ContentType", contentType);
			writer.WriteEndElement();
		}

		private static void WritePackageRelationships(ZipArchive archive)
		{
			using var writer = CreateEntryWriter(archive, "_rels/.rels");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("Relationships", PackageRelationshipNs);
			WriteRelationship(writer, "rId1", RelationshipNs + "/officeDocument", "xl/workbook.xml");
			writer.WriteEndElement();
		}

		private static void WriteWorkbook(ZipArchive archive, string sheetName)
		{
			using var writer = CreateEntryWriter(archive, "xl/workbook.xml");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("workbook", MainNs);
			writer.WriteAttributeString("xmlns", "r", null, RelationshipNs);
			writer.WriteStartElement("sheets");
			writer.WriteStartElement("sheet");
			writer.WriteAttributeString("name", sheetName);
			writer.WriteAttributeString("sheetId", "1");
			writer.WriteAttributeString("r", "id", RelationshipNs, "rId1");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		private static void WriteWorkbookRelationships(ZipArchive archive)
		{
			using var writer = CreateEntryWriter(archive, "xl/_rels/workbook.xml.rels");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("Relationships", PackageRelationshipNs);
			WriteRelationship(writer, "rId1", RelationshipNs + "/worksheet", "worksheets/sheet1.xml");
			WriteRelationship(writer, "rId2", RelationshipNs + "/styles", "styles.xml");
			writer.WriteEndElement();
		}

		private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
		{
			writer.WriteStartElement("Relationship");
			writer.WriteAttributeString("Id", id);
			writer.WriteAttributeString("Type", type);
			writer.WriteAttributeString("Target", target);
			writer.WriteEndElement();
		}

		private static void WriteStyles(ZipArchive archive)
		{
			using var writer = CreateEntryWriter(archive, "xl/styles.xml");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("styleSheet", MainNs);
			writer.WriteStartElement("fonts");
			writer.WriteAttributeString("count", "2");
			writer.WriteStartElement("font");
			writer.WriteEndElement();
			writer.WriteStartElement("font");
			writer.WriteStartElement("b");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteStartElement("fills");
			writer.WriteAttributeString("count", "1");
			writer.WriteStartElement("fill");
			writer.WriteStartElement("patternFill");
			writer.WriteAttributeString("patternType", "none");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteStartElement("borders");
			writer.WriteAttributeString("count", "1");
			writer.WriteStartElement("border");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteStartElement("cellStyleXfs");
			writer.WriteAttributeString("count", "1");
			writer.WriteStartElement("xf");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteStartElement("cellXfs");
			writer.WriteAttributeString("count", "2");
			writer.WriteStartElement("xf");
			writer.WriteEndElement();
			writer.WriteStartElement("xf");
			writer.WriteAttributeString("fontId", "1");
			writer.WriteAttributeString("applyFont", "1");
			writer.WriteEndElement();
			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<(FileListExportColumn Column, string Header)> columns, IEnumerable<FileListExportRow> rows)
		{
			using var writer = CreateEntryWriter(archive, "xl/worksheets/sheet1.xml");
			writer.WriteStartDocument(true);
			writer.WriteStartElement("worksheet", MainNs);
			writer.WriteStartElement("sheetData");

			var rowIndex = 1;
			WriteRow(writer, columns.Count, rowIndex++, (index, currentRow) => WriteInlineStringCell(writer, index, currentRow, columns[index].Header, bold: true));

			foreach (var row in rows)
			{
				WriteRow(writer, columns.Count, rowIndex, (index, currentRow) => WriteDataCell(writer, columns[index].Column, index, currentRow, row));
				rowIndex++;
			}

			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		private static void WriteRow(XmlWriter writer, int columnCount, int rowIndex, Action<int, int> writeCell)
		{
			writer.WriteStartElement("row");
			writer.WriteAttributeString("r", rowIndex.ToString());
			for (var index = 0; index < columnCount; index++)
				writeCell(index, rowIndex);
			writer.WriteEndElement();
		}

		private static void WriteDataCell(XmlWriter writer, FileListExportColumn column, int columnIndex, int rowIndex, FileListExportRow row)
		{
			if (column == FileListExportColumn.SizeBytes)
			{
				WriteCellStart(writer, columnIndex, rowIndex);
				if (row.SizeBytes is { } size)
				{
					writer.WriteElementString("v", size.ToString());
					writer.WriteEndElement();
				}
				else
				{
					// Unknown size is a truly empty cell, never a zero.
					writer.WriteEndElement();
				}
			}
			else
			{
				WriteInlineStringCell(writer, columnIndex, rowIndex, FileListCsvWriter.GetCellText(column, row), bold: false);
			}
		}

		private static void WriteInlineStringCell(XmlWriter writer, int columnIndex, int rowIndex, string text, bool bold)
		{
			WriteCellStart(writer, columnIndex, rowIndex);
			writer.WriteAttributeString("t", "inlineStr");
			if (bold)
				writer.WriteAttributeString("s", "1");
			writer.WriteStartElement("is");
			writer.WriteElementString("t", text);
			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		private static void WriteCellStart(XmlWriter writer, int columnIndex, int rowIndex)
		{
			writer.WriteStartElement("c");
			writer.WriteAttributeString("r", GetCellReference(columnIndex, rowIndex));
		}

		private static string GetCellReference(int columnIndex, int rowIndex)
			=> GetColumnLetters(columnIndex) + rowIndex.ToString();

		private static string GetColumnLetters(int columnIndex)
		{
			var letters = new StringBuilder();
			columnIndex++;
			while (columnIndex > 0)
			{
				columnIndex--;
				letters.Insert(0, (char)('A' + columnIndex % 26));
				columnIndex /= 26;
			}
			return letters.ToString();
		}

		private static XmlWriter CreateEntryWriter(ZipArchive archive, string entryName)
		{
			var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
			return XmlWriter.Create(entry.Open(), new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = false, CloseOutput = true });
		}
	}
}
