// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class FileListExportSorterTests
	{
		private static FileListExportRow CreateRow(
			string name = "a",
			long? sizeBytes = null,
			DateTimeOffset? dateModified = null,
			DateTimeOffset? dateCreated = null,
			string? itemType = null,
			string? extension = null,
			bool isFolder = false,
			string fullPath = "")
			=> new(
				name,
				extension,
				itemType,
				sizeBytes,
				null,
				dateCreated,
				dateModified,
				fullPath.Length > 0 ? fullPath : @"C:\A\" + name,
				@".\" + name,
				isFolder,
				false,
				null);

		private static List<string> SortNames(SortOption option, SortDirection direction, params FileListExportRow[] rows)
			=> rows.OrderBy(row => row, FileListExportSorter.Create(option, direction)).Select(row => row.Name).ToList();

		[TestMethod]
		public void FoldersAlwaysFirst_RegardlessOfOptionAndDirection()
		{
			var fileBig = CreateRow(name: "a.txt", sizeBytes: 9);
			var fileSmall = CreateRow(name: "b.txt", sizeBytes: 1);
			var folder = CreateRow(name: "zzz", isFolder: true);

			var order = SortNames(SortOption.Size, SortDirection.Descending, fileBig, fileSmall, folder);

			CollectionAssert.AreEqual(new[] { "zzz", "a.txt", "b.txt" }, order);
		}

		[TestMethod]
		public void SortsByName_BothDirections()
		{
			var order = SortNames(SortOption.Name, SortDirection.Ascending,
				CreateRow(name: "b.txt"), CreateRow(name: "c.txt"), CreateRow(name: "a.txt"));
			CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, order);

			order = SortNames(SortOption.Name, SortDirection.Descending,
				CreateRow(name: "b.txt"), CreateRow(name: "c.txt"), CreateRow(name: "a.txt"));
			CollectionAssert.AreEqual(new[] { "c.txt", "b.txt", "a.txt" }, order);
		}

		[TestMethod]
		public void SortsBySize_Descending()
		{
			var order = SortNames(SortOption.Size, SortDirection.Descending,
				CreateRow(name: "small", sizeBytes: 1),
				CreateRow(name: "big", sizeBytes: 100),
				CreateRow(name: "mid", sizeBytes: 10));
			CollectionAssert.AreEqual(new[] { "big", "mid", "small" }, order);
		}

		[TestMethod]
		public void SortsByDateModified_NullDatesSortLast()
		{
			var day1 = new DateTimeOffset(new DateTime(2026, 1, 1));
			var day2 = new DateTimeOffset(new DateTime(2026, 1, 2));

			var order = SortNames(SortOption.DateModified, SortDirection.Ascending,
				CreateRow(name: "unknown", dateModified: null),
				CreateRow(name: "newer", dateModified: day2),
				CreateRow(name: "older", dateModified: day1));
			CollectionAssert.AreEqual(new[] { "older", "newer", "unknown" }, order);
		}

		[TestMethod]
		public void SortsByExtension()
		{
			var order = SortNames(SortOption.FileType, SortDirection.Ascending,
				CreateRow(name: "z.a", extension: ".a"),
				CreateRow(name: "a.c", extension: ".c"),
				CreateRow(name: "m.b", extension: ".b"));
			CollectionAssert.AreEqual(new[] { "z.a", "m.b", "a.c" }, order);
		}

		[TestMethod]
		public void SortsByPath()
		{
			var order = SortNames(SortOption.Path, SortDirection.Ascending,
				CreateRow(name: "sub", fullPath: @"C:\A\Sub2\c.txt"),
				CreateRow(name: "root", fullPath: @"C:\A\a.txt"),
				CreateRow(name: "mid", fullPath: @"C:\A\Sub1\b.txt"));
			CollectionAssert.AreEqual(new[] { "root", "mid", "sub" }, order);
		}

		[TestMethod]
		public void UnsupportedOptionFallsBackToName()
		{
			var order = SortNames(SortOption.SyncStatus, SortDirection.Ascending,
				CreateRow(name: "b.txt"), CreateRow(name: "a.txt"));
			CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, order);

			order = SortNames(SortOption.DateDeleted, SortDirection.Ascending,
				CreateRow(name: "b.txt"), CreateRow(name: "a.txt"));
			CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, order);
		}
	}
}
