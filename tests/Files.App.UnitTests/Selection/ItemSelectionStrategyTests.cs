// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.UserControls.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Selection
{
	[TestClass]
	public sealed class ItemSelectionStrategyTests
	{
		// Regression guard for the incremental rectangle-selection path: applying a
		// strategy only to items whose rectangle intersection changed must produce the
		// exact same final selection (content and order) as re-applying it to every
		// item on every pointer move, which is what the old code did.
		[TestMethod]
		public void Strategies_IncrementalApplication_MatchesFullApplication()
		{
			var strategies = new (string Name, Func<List<object>, List<object>, ItemSelectionStrategy> Factory)[]
			{
				("ignore", (selection, prev) => new IgnorePreviousItemSelectionStrategy(selection)),
				("extend", (selection, prev) => new ExtendPreviousItemSelectionStrategy(selection, prev)),
				("invert", (selection, prev) => new InvertPreviousItemSelectionStrategy(selection, prev)),
			};

			foreach (var (name, factory) in strategies)
			{
				foreach (var previousSelection in BuildPreviousSelections())
				{
					foreach (var moves in BuildRectangleMoves())
					{
						var allItems = Enumerable.Range(0, 40).Select(i => (object)i).ToList();

						var full = RunDrag(factory, allItems, previousSelection, moves, incrementalOnly: false);
						var incremental = RunDrag(factory, allItems, previousSelection, moves, incrementalOnly: true);

						Assert.IsTrue(full.SequenceEqual(incremental),
							$"Strategy '{name}' diverged with prev=[{string.Join(",", previousSelection)}], moves={DescribeMoves(moves)}: " +
							$"full=[{string.Join(",", full)}] incremental=[{string.Join(",", incremental)}]");
					}
				}
			}
		}

		[TestMethod]
		public void IgnorePrevious_ReportsOnlyAppliedChanges()
		{
			var selection = new List<object>();
			var a = new object();
			var b = new object();
			var strategy = new IgnorePreviousItemSelectionStrategy(selection);

			strategy.HandleIntersectionWithItem(a);
			CollectionAssert.AreEqual(new[] { a }, strategy.AppliedAddedItems.ToArray());
			CollectionAssert.AreEqual(Array.Empty<object>(), strategy.AppliedRemovedItems.ToArray());

			// Re-applying the same state is a no-op and must not duplicate the report
			strategy.HandleIntersectionWithItem(a);
			Assert.AreEqual(1, strategy.AppliedAddedItems.Count);

			strategy.HandleIntersectionWithItem(b);
			CollectionAssert.AreEqual(new[] { a, b }, strategy.AppliedAddedItems.ToArray());

			strategy.HandleNoIntersectionWithItem(a);
			CollectionAssert.AreEqual(new[] { a }, strategy.AppliedRemovedItems.ToArray());

			strategy.ResetAppliedChanges();
			Assert.AreEqual(0, strategy.AppliedAddedItems.Count);
			Assert.AreEqual(0, strategy.AppliedRemovedItems.Count);
		}

		[TestMethod]
		public void ExtendPrevious_KeepsPreviousItemsAndReportsAppliedChanges()
		{
			var kept = new object();
			var added = new object();
			var previous = new List<object> { kept };
			var selection = previous.ToList();
			var strategy = new ExtendPreviousItemSelectionStrategy(selection, previous);

			strategy.HandleIntersectionWithItem(kept);
			Assert.AreEqual(0, strategy.AppliedAddedItems.Count);

			strategy.HandleIntersectionWithItem(added);
			CollectionAssert.AreEqual(new[] { added }, strategy.AppliedAddedItems.ToArray());

			// Items from the previous selection are never removed when they leave the rectangle
			strategy.HandleNoIntersectionWithItem(kept);
			CollectionAssert.AreEqual(new[] { kept, added }, selection);
			Assert.AreEqual(0, strategy.AppliedRemovedItems.Count);

			strategy.HandleNoIntersectionWithItem(added);
			CollectionAssert.AreEqual(new[] { added }, strategy.AppliedRemovedItems.ToArray());
			CollectionAssert.AreEqual(new[] { kept }, selection);
		}

		[TestMethod]
		public void InvertPrevious_TogglesRelativePreviousSelectionAndReportsAppliedChanges()
		{
			var selectedBefore = new object();
			var unselectedBefore = new object();
			var previous = new List<object> { selectedBefore };
			var selection = previous.ToList();
			var strategy = new InvertPreviousItemSelectionStrategy(selection, previous);

			// Intersecting inverts the previous state
			strategy.HandleIntersectionWithItem(selectedBefore);
			CollectionAssert.AreEqual(new[] { selectedBefore }, strategy.AppliedRemovedItems.ToArray());

			strategy.HandleIntersectionWithItem(unselectedBefore);
			CollectionAssert.AreEqual(new[] { unselectedBefore }, strategy.AppliedAddedItems.ToArray());

			// Leaving the rectangle restores the previous state
			strategy.HandleNoIntersectionWithItem(selectedBefore);
			CollectionAssert.AreEqual(new[] { unselectedBefore, selectedBefore }, selection);

			strategy.HandleNoIntersectionWithItem(unselectedBefore);
			CollectionAssert.AreEqual(new[] { selectedBefore, unselectedBefore }, strategy.AppliedRemovedItems.ToArray());
			CollectionAssert.AreEqual(new[] { selectedBefore }, selection);
		}

		private static List<object> RunDrag(
			Func<List<object>, List<object>, ItemSelectionStrategy> createStrategy,
			List<object> allItems,
			List<object> previousSelection,
			IReadOnlyList<HashSet<object>> rectangleMoves,
			bool incrementalOnly)
		{
			var selection = previousSelection.ToList();
			var strategy = createStrategy(selection, previousSelection);
			// Mirrors the press sequence: the plain-drag strategy clears the selection on
			// press, the Ctrl/Shift strategies keep it, and StartSelection runs on the
			// first move before any intersection is handled.
			strategy.HandleNoItemSelected();
			strategy.StartSelection();

			var lastIntersecting = new HashSet<object>();
			foreach (var move in rectangleMoves)
			{
				foreach (var item in allItems)
				{
					var isIntersecting = move.Contains(item);
					if (incrementalOnly && isIntersecting == lastIntersecting.Contains(item))
						continue;

					if (isIntersecting)
						strategy.HandleIntersectionWithItem(item);
					else
						strategy.HandleNoIntersectionWithItem(item);
				}

				lastIntersecting = [.. move];
			}

			return selection;
		}

		private static IEnumerable<List<object>> BuildPreviousSelections()
		{
			yield return [];
			yield return [0, 1, 2];
			yield return [5, 17, 33, 39];
		}

		private static IEnumerable<List<HashSet<object>>> BuildRectangleMoves()
		{
			// Expanding downward drag
			yield return
			[
				ToSet(0, 1),
				ToSet(0, 1, 2, 3),
				ToSet(0, 1, 2, 3, 4, 5),
			];

			// Shrink back after growing
			yield return
			[
				ToSet(0, 1, 2, 3),
				ToSet(0, 1, 2, 3, 4, 5),
				ToSet(1, 2, 3),
				ToSet(3),
			];

			// Moving window that leaves items behind
			yield return
			[
				ToSet(0, 1, 2),
				ToSet(2, 3, 4),
				ToSet(4, 5, 6),
			];

			// Empty rectangle after a selection, then everything
			yield return
			[
				ToSet(10, 11),
				ToSet(),
				ToSet(Enumerable.Range(0, 40)),
			];

			// Seeded random masks
			var random = new Random(20261112);
			var moves = new List<HashSet<object>>();
			for (var m = 0; m < 8; m++)
			{
				var mask = 0;
				for (var i = 0; i < 40; i++)
				{
					if (random.Next(0, 2) == 0)
						mask |= 1 << i;
				}

				moves.Add(Enumerable.Range(0, 40).Where(i => (mask & (1 << i)) != 0).Select(i => (object)i).ToHashSet());
			}

			yield return moves;
		}

		private static HashSet<object> ToSet(params int[] items) =>
			items.Select(i => (object)i).ToHashSet();

		private static HashSet<object> ToSet(IEnumerable<int> items) =>
			items.Select(i => (object)i).ToHashSet();

		private static string DescribeMoves(IReadOnlyList<HashSet<object>> moves) =>
			string.Join(";", moves.Select(move => string.Join(",", move.Select(item => (int)item))));
	}
}
