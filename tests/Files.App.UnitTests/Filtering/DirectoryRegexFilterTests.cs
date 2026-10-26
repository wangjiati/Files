// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryRegexFilterTests
	{
		[TestMethod]
		public void RawName_MatchesExtensionEvenWhenDisplayNameHidesIt()
		{
			var items = new[] { CreateItem(1, "report.txt", displayName: "report") };
			var spec = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"\.txt$", DirectoryRegexTarget.RawName, IgnoreCase: true),
			};

			Assert.AreEqual(1, Evaluate(spec, items).Count);

			var displaySpec = spec with
			{
				Regex = new DirectoryRegexOptions(@"\.txt$", DirectoryRegexTarget.DisplayName, IgnoreCase: true),
			};

			Assert.AreEqual(0, Evaluate(displaySpec, items).Count);
		}

		[TestMethod]
		public void DisplayName_MatchesVisibleName()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt", displayName: "report"),
				CreateItem(2, "notes.txt"),
			};
			var spec = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions("^rep", DirectoryRegexTarget.DisplayName, IgnoreCase: false),
			};

			CollectionAssert.AreEqual(new long[] { 1 }, Evaluate(spec, items).ToArray());
		}

		[TestMethod]
		public void FullPath_MatchesDirectoryPart()
		{
			var items = new[]
			{
				CreateItem(1, "a.txt", path: @"C:\Work\Reports\a.txt"),
				CreateItem(2, "b.txt", path: @"C:\Work\Notes\b.txt"),
			};
			var spec = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"\\Reports\\", DirectoryRegexTarget.FullPath, IgnoreCase: true),
			};

			CollectionAssert.AreEqual(new long[] { 1 }, Evaluate(spec, items).ToArray());
		}

		[TestMethod]
		public void IgnoreCase_ControlsMatching()
		{
			var items = new[] { CreateItem(1, "Report.txt") };
			var insensitive = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions("^report", DirectoryRegexTarget.RawName, IgnoreCase: true),
			};
			var sensitive = insensitive with
			{
				Regex = new DirectoryRegexOptions("^report", DirectoryRegexTarget.RawName, IgnoreCase: false),
			};

			Assert.AreEqual(1, Evaluate(insensitive, items).Count);
			Assert.AreEqual(0, Evaluate(sensitive, items).Count);
		}

		[TestMethod]
		public void InvalidPattern_ReturnsErrorInsteadOfThrowing()
		{
			var result = DirectoryFilterEvaluator.EvaluateDetailed(
				[CreateItem(1, "a.txt")],
				new DirectoryFilterSpec { Regex = new DirectoryRegexOptions("(", DirectoryRegexTarget.RawName, true) });

			Assert.AreEqual(DirectoryRegexError.InvalidPattern, result.Error);
			Assert.AreEqual(0, result.ItemIds.Count);
			Assert.IsFalse(result.IsSuccess);
		}

		[TestMethod]
		public void LongPattern_ReturnsError()
		{
			var pattern = new string('a', DirectoryRegexFilter.MaxPatternLength + 1);
			var result = DirectoryFilterEvaluator.EvaluateDetailed(
				[CreateItem(1, "a.txt")],
				new DirectoryFilterSpec { Regex = new DirectoryRegexOptions(pattern, DirectoryRegexTarget.RawName, true) });

			Assert.AreEqual(DirectoryRegexError.PatternTooLong, result.Error);
			Assert.AreEqual(0, result.ItemIds.Count);
		}

		[TestMethod]
		public void CatastrophicBacktracking_ReturnsTimeoutWithoutPartialResults()
		{
			var items = Enumerable.Range(1, 5)
				.Select(id => CreateItem(id, new string('a', 64) + "!"))
				.ToArray();
			var result = DirectoryFilterEvaluator.EvaluateDetailed(
				items,
				new DirectoryFilterSpec { Regex = new DirectoryRegexOptions("(a+)+$", DirectoryRegexTarget.RawName, true) });

			Assert.AreEqual(DirectoryRegexError.Timeout, result.Error);
			Assert.AreEqual(0, result.ItemIds.Count);
		}

		[TestMethod]
		public void Cancellation_ThrowsBeforeCommittingResults()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();

			var wasCancelled = false;
			try
			{
				DirectoryFilterEvaluator.EvaluateDetailed(
					[CreateItem(1, "a.txt")],
					new DirectoryFilterSpec { Regex = new DirectoryRegexOptions("a", DirectoryRegexTarget.RawName, true) },
					cancellation.Token);
			}
			catch (OperationCanceledException)
			{
				wasCancelled = true;
			}

			Assert.IsTrue(wasCancelled);
		}

		[TestMethod]
		public void Regex_CombinesWithStructuredConditions()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt", extension: ".txt", size: 10),
				CreateItem(2, "report.png", extension: ".png", size: 10),
				CreateItem(3, "notes.txt", extension: ".txt", size: 10),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".txt"] }],
				Regex = new DirectoryRegexOptions("^report", DirectoryRegexTarget.RawName, true),
			};

			CollectionAssert.AreEqual(new long[] { 1 }, Evaluate(spec, items).ToArray());
		}

		private static IReadOnlyList<long> Evaluate(DirectoryFilterSpec spec, IReadOnlyList<DirectoryItemFilterSnapshot> items)
		{
			var result = DirectoryFilterEvaluator.EvaluateDetailed(items, spec);

			Assert.AreEqual(DirectoryRegexError.None, result.Error);

			return result.ItemIds;
		}

		private static DirectoryItemFilterSnapshot CreateItem(
			long id,
			string rawName,
			string? displayName = null,
			string? path = null,
			string? extension = null,
			long? size = 0)
		{
			return new DirectoryItemFilterSnapshot(
				id,
				rawName,
				displayName ?? rawName,
				path ?? @$"C:\Items\{rawName}",
				extension,
				"File",
				[],
				size,
				null,
				null,
				false);
		}
	}
}
