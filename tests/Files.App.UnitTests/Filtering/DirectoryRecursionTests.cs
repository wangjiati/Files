// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryRecursionTests
	{
		private sealed class TestNode(string path)
		{
			public string Path { get; } = path;

			public List<TestNode> Children { get; } = [];

			public TestNode Add(TestNode child)
			{
				Children.Add(child);

				return this;
			}
		}

		private static TestNode Node(string path)
			=> new(path);

		private static IReadOnlyList<DirectoryRecursionEntry<TestNode>> Flatten(IReadOnlyList<TestNode> roots, int maxDepth)
			=> DirectoryRecursion.Flatten(roots, node => node.Children, node => node.Path, maxDepth);

		[TestMethod]
		public void Flatten_DepthOne_IncludesFirstLevelOnly()
		{
			var roots = new[]
			{
				Node(@"C:\root\a").Add(Node(@"C:\root\a\b")),
				Node(@"C:\root\c"),
			};

			var result = Flatten(roots, 1);

			CollectionAssert.AreEqual(new[] { "a", "c" }, result.Select(entry => entry.RelativePath).ToArray());
		}

		[TestMethod]
		public void Flatten_DepthTwo_AddsRelativePaths()
		{
			var roots = new[]
			{
				Node(@"C:\root\a").Add(Node(@"C:\root\a\b").Add(Node(@"C:\root\a\b\c"))),
			};

			var result = Flatten(roots, 2);

			CollectionAssert.AreEqual(new[] { "a", @"a\b" }, result.Select(entry => entry.RelativePath).ToArray());
		}

		[TestMethod]
		[DataRow(0, 1)]
		[DataRow(-5, 1)]
		[DataRow(999, 64)]
		public void Flatten_ClampsDepth(int requested, int effective)
		{
			var root = Node(@"C:\root\0");
			var current = root;

			// A chain longer than the largest effective depth proves the requested value is clamped.
			for (var level = 1; level <= 70; level++)
			{
				var child = Node($@"C:\root\{level}");
				current.Add(child);
				current = child;
			}

			var result = Flatten([root], requested);

			Assert.AreEqual(effective, result.Count);
		}

		[TestMethod]
		public void Flatten_CycleGuard_StopsOnRepeatedPath()
		{
			var root = Node(@"C:\root\a");
			var loop = Node(@"C:\root\a\loop");
			root.Add(loop);
			loop.Add(root);

			var result = Flatten([root], 8);

			CollectionAssert.AreEqual(new[] { "a", @"a\loop" }, result.Select(entry => entry.RelativePath).ToArray());
		}
	}
}
