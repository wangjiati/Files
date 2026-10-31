// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using Windows.Storage;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class FileListExportRowFactoryTests
	{
		private readonly string root = Path.Combine(Path.GetTempPath(), "files-export-tests-" + Guid.NewGuid().ToString("N"));

		[TestCleanup]
		public void Cleanup()
		{
			if (Directory.Exists(root))
				Directory.Delete(root, true);
		}

		private static ListedItem CreateFileItem(string name, string path, long sizeBytes, string sizeDisplay)
			=> new()
			{
				ItemNameRaw = name,
				ItemPath = path,
				PrimaryItemAttribute = StorageItemTypes.File,
				FileSizeBytes = sizeBytes,
				FileSize = sizeDisplay,
			};

		[TestMethod]
		public void FromListedItem_FolderSizeStaysEmpty()
		{
			var item = new ListedItem
			{
				ItemNameRaw = "Sub",
				ItemPath = @"C:\A\Sub",
				PrimaryItemAttribute = StorageItemTypes.Folder,
				FileSize = string.Empty,
				FileSizeBytes = 0,
			};

			var row = FileListExportRowFactory.FromListedItem(item, @"C:\A", null);

			Assert.IsNull(row.SizeBytes);
			Assert.IsTrue(row.IsFolder);
			Assert.AreEqual("Sub", row.Name);
		}

		[TestMethod]
		public void FromListedItem_ZeroByteFileExportsZero()
		{
			var item = CreateFileItem("empty.txt", @"C:\A\empty.txt", 0, "0 bytes");

			var row = FileListExportRowFactory.FromListedItem(item, @"C:\A", "红; 蓝");

			Assert.AreEqual(0L, row.SizeBytes);
			Assert.AreEqual("empty.txt", row.Name);
			Assert.AreEqual("红; 蓝", row.TagNames);
		}

		[TestMethod]
		public void FromListedItem_UnknownDatesBecomeNull()
		{
			var item = CreateFileItem("a.txt", @"C:\A\a.txt", 5, "5 bytes");

			var row = FileListExportRowFactory.FromListedItem(item, @"C:\A", null);

			Assert.IsNull(row.DateCreated);
			Assert.IsNull(row.DateModified);
		}

		[TestMethod]
		public void FormatDate_KnownLocalDateFormatsAsIso()
		{
			var local = new DateTimeOffset(new DateTime(2026, 10, 31, 15, 4, 5));

			Assert.AreEqual("2026-10-31 15:04:05", FileListExportRowFactory.FormatDate(local));
			Assert.AreEqual(string.Empty, FileListExportRowFactory.FormatDate(null));
		}

		[TestMethod]
		public void GetRelativePath_RootItemIsDotPrefixed()
		{
			Assert.AreEqual(@".\b.txt", FileListExportRowFactory.GetRelativePath(@"C:\A", @"C:\A\b.txt"));
			Assert.AreEqual(@".\Sub\c.txt", FileListExportRowFactory.GetRelativePath(@"C:\A", @"C:\A\Sub\c.txt"));
		}

		[TestMethod]
		public void FromFileSystemInfo_BuildsRowFromFindData()
		{
			Directory.CreateDirectory(root);
			File.WriteAllText(Path.Combine(root, "d.txt"), "abc");
			Directory.CreateDirectory(Path.Combine(root, "Sub"));
			File.WriteAllText(Path.Combine(root, "h.log"), "x");
			File.WriteAllText(Path.Combine(root, "noext"), "y");
			File.SetAttributes(Path.Combine(root, "h.log"), System.IO.FileAttributes.Hidden);

			var fileRow = FileListExportRowFactory.FromFileSystemInfo(new FileInfo(Path.Combine(root, "d.txt")), root, null);
			Assert.AreEqual(3L, fileRow.SizeBytes);
			Assert.IsFalse(fileRow.IsFolder);
			Assert.IsFalse(fileRow.IsHidden);
			Assert.AreEqual("d.txt", fileRow.Name);
			Assert.AreEqual(".txt", fileRow.Extension);
			Assert.AreEqual(@".\d.txt", fileRow.RelativePath);
			Assert.AreEqual(Path.Combine(root, "d.txt"), fileRow.FullPath);
			Assert.IsNotNull(fileRow.DateCreated);
			Assert.IsNotNull(fileRow.DateModified);

			var dirRow = FileListExportRowFactory.FromFileSystemInfo(new DirectoryInfo(Path.Combine(root, "Sub")), root, null);
			Assert.IsNull(dirRow.SizeBytes);
			Assert.IsTrue(dirRow.IsFolder);
			Assert.IsNull(dirRow.Extension);

			var hiddenRow = FileListExportRowFactory.FromFileSystemInfo(new FileInfo(Path.Combine(root, "h.log")), root, null);
			Assert.IsTrue(hiddenRow.IsHidden);

			var noExtRow = FileListExportRowFactory.FromFileSystemInfo(new FileInfo(Path.Combine(root, "noext")), root, null);
			Assert.IsNull(noExtRow.Extension);
		}
	}
}
