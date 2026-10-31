// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class FileListExportEnumeratorTests
	{
		private readonly string root = Path.Combine(Path.GetTempPath(), "files-export-enum-" + Guid.NewGuid().ToString("N"));
		private readonly FileListExportContext context = new();

		[TestInitialize]
		public void Setup()
		{
			Directory.CreateDirectory(root);
		}

		[TestCleanup]
		public void Cleanup()
		{
			if (!Directory.Exists(root))
				return;

			// A junction must be removed as a link before the tree can be deleted.
			var junction = Path.Combine(root, "Loop");
			if (Directory.Exists(junction))
			{
				try { Directory.Delete(junction, false); }
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}

			try { Directory.Delete(root, true); }
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		private FileListExportRow CreateRootRow(FileSystemInfo info)
			=> FileListExportRowFactory.FromFileSystemInfo(info, root, null);

		private List<FileListExportRow> SortRows(IEnumerable<FileListExportRow> rows)
			=> rows.OrderBy(row => row, FileListExportSorter.Create(SortOption.Name, SortDirection.Ascending)).ToList();

		[TestMethod]
		public void Recursive_WalksFolderRowBeforeChildrenInSortedOrder()
		{
			File.WriteAllText(Path.Combine(root, "a.txt"), "a");
			Directory.CreateDirectory(Path.Combine(root, "Sub1"));
			File.WriteAllText(Path.Combine(root, "Sub1", "c.txt"), "c");
			Directory.CreateDirectory(Path.Combine(root, "Sub2"));
			File.WriteAllText(Path.Combine(root, "Sub2", "b.txt"), "b");

			var rootRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos().Select(CreateRootRow));

			var names = FileListExportEnumerator.EnumerateRecursive(
				rootRows, root, SortOption.Name, SortDirection.Ascending, null, new Dictionary<string, IReadOnlyList<string>>(), context)
				.Select(row => row.RelativePath).ToList();

			CollectionAssert.AreEqual(new[] { @".\Sub1", @".\Sub2", @".\a.txt", @".\Sub1\c.txt", @".\Sub2\b.txt" }, names);
		}

		[TestMethod]
		public void Recursive_AppliesFilterSpecToSubfolderItemsOnly()
		{
			Directory.CreateDirectory(Path.Combine(root, "Dir"));
			File.WriteAllText(Path.Combine(root, "Dir", "b.txt"), "b");
			File.WriteAllText(Path.Combine(root, "Dir", "x.txt"), "x");
			File.WriteAllText(Path.Combine(root, "z.txt"), "z");

			// The view (current level) already filtered nothing matching "b" at root: Dir and z.txt stay unmatched.
			var viewRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos()
				.Select(CreateRootRow)
				.Where(row => row.Name.Contains('b')));
			Assert.AreEqual(0, viewRows.Count, "precondition: no root-level item matches");

			var spec = new DirectoryFilterSpec { NameContains = "b" };

			var names = FileListExportEnumerator.EnumerateRecursive(
				viewRows, root, SortOption.Name, SortDirection.Ascending, spec, new Dictionary<string, IReadOnlyList<string>>(), context)
				.Select(row => row.RelativePath).ToList();

			// The Dir folder row itself is filtered out, but its subtree is still walked and filtered.
			CollectionAssert.AreEqual(new[] { @".\Dir\b.txt" }, names);
		}

		[TestMethod]
		public void Recursive_JunctionIsEmittedButNotTraversed()
		{
			File.WriteAllText(Path.Combine(root, "a.txt"), "a");
			var junction = Path.Combine(root, "Loop");
			var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{root}\"") { CreateNoWindow = true, UseShellExecute = false });
			process!.WaitForExit();
			Assert.AreEqual(0, process.ExitCode, "precondition: junction created");

			var rootRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos().Select(CreateRootRow));

			var names = FileListExportEnumerator.EnumerateRecursive(
				rootRows, root, SortOption.Name, SortDirection.Ascending, null, new Dictionary<string, IReadOnlyList<string>>(), context)
				.Select(row => row.RelativePath).ToList();

			CollectionAssert.AreEqual(new[] { @".\Loop", @".\a.txt" }, names, "junction row is emitted once and never traversed");
		}

		[TestMethod]
		public void Recursive_UnlistableDirectoryIncrementsSkipAndContinues()
		{
			Directory.CreateDirectory(Path.Combine(root, "Bad"));
			Directory.CreateDirectory(Path.Combine(root, "Good"));
			File.WriteAllText(Path.Combine(root, "Good", "ok.txt"), "ok");

			var rootRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos().Select(CreateRootRow));
			Func<string, IReadOnlyList<FileSystemInfo>> listDirectory = path
				=> path.EndsWith("Bad", StringComparison.OrdinalIgnoreCase)
					? throw new UnauthorizedAccessException("denied")
					: new List<FileSystemInfo>(new DirectoryInfo(path).EnumerateFileSystemInfos());

			var names = FileListExportEnumerator.EnumerateRecursive(
				rootRows, root, SortOption.Name, SortDirection.Ascending, null, new Dictionary<string, IReadOnlyList<string>>(), context, listDirectory)
				.Select(row => row.RelativePath).ToList();

			CollectionAssert.AreEqual(new[] { @".\Bad", @".\Good", @".\Good\ok.txt" }, names);
			Assert.AreEqual(1L, context.SkippedCount);
		}

		[TestMethod]
		public void Recursive_HiddenFilesAreIncluded()
		{
			Directory.CreateDirectory(Path.Combine(root, "Sub"));
			var hiddenPath = Path.Combine(root, "Sub", "h.log");
			File.WriteAllText(hiddenPath, "h");
			File.SetAttributes(hiddenPath, System.IO.FileAttributes.Hidden);

			var rootRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos().Select(CreateRootRow));

			var hidden = FileListExportEnumerator.EnumerateRecursive(
				rootRows, root, SortOption.Name, SortDirection.Ascending, null, new Dictionary<string, IReadOnlyList<string>>(), context)
				.Single(row => row.Name == "h.log");

			Assert.IsTrue(hidden.IsHidden);
		}

		[TestMethod]
		public void Recursive_EntryPropertyFailureSkipsRowAndContinues()
		{
			Directory.CreateDirectory(Path.Combine(root, "Sub"));
			File.WriteAllText(Path.Combine(root, "Sub", "ok.txt"), "ok");

			var rootRows = SortRows(new DirectoryInfo(root).EnumerateFileSystemInfos().Select(CreateRootRow));
			Func<string, IReadOnlyList<FileSystemInfo>> listDirectory = path
				=> new List<FileSystemInfo>(new DirectoryInfo(path).EnumerateFileSystemInfos())
				{
					new ThrowingEntry(),
				};

			var names = FileListExportEnumerator.EnumerateRecursive(
				rootRows, root, SortOption.Name, SortDirection.Ascending, null, new Dictionary<string, IReadOnlyList<string>>(), context, listDirectory)
				.Select(row => row.Name).ToList();

			Assert.IsTrue(names.Contains("ok.txt"), "healthy sibling rows are still exported");
			Assert.IsFalse(names.Contains("broken.txt"), "unreadable entries are skipped");
			Assert.AreEqual(1L, context.SkippedCount);
		}

		private sealed class ThrowingEntry : FileSystemInfo
		{
			public override bool Exists => false;

			public override string Name
				=> throw new UnauthorizedAccessException("denied");

			public override string FullName => string.Empty;

			public override void Delete() { }
		}

		[TestMethod]
		public void CurrentLevel_MapsListedItemsInOrder()
		{
			var first = new ListedItem { ItemNameRaw = "a.txt", ItemPath = Path.Combine(root, "a.txt"), PrimaryItemAttribute = StorageItemTypes.File };
			var second = new ListedItem { ItemNameRaw = "Sub", ItemPath = Path.Combine(root, "Sub"), PrimaryItemAttribute = StorageItemTypes.Folder };

			var rows = FileListExportEnumerator.EnumerateCurrentLevel([second, first], root, item => item.ItemNameRaw == "a.txt" ? "红; 蓝" : null).ToList();

			Assert.AreEqual(2, rows.Count);
			Assert.AreEqual("Sub", rows[0].Name);
			Assert.IsTrue(rows[0].IsFolder);
			Assert.IsNull(rows[0].TagNames);
			Assert.AreEqual("a.txt", rows[1].Name);
			Assert.AreEqual("红; 蓝", rows[1].TagNames);
		}
	}
}
