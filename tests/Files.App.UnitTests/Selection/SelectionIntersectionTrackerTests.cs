// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.UserControls.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Files.App.UnitTests.Selection
{
	[TestClass]
	public sealed class SelectionIntersectionTrackerTests
	{
		private static Rectangle Rect(int top, int bottom) => new(0, top, 10, bottom - top);

		[TestMethod]
		public void ApplyIntersectionChanges_OnlyChangedItemsGetStrategyCalls()
		{
			var itemsPosition = new Dictionary<object, Rectangle>
			{
				["a"] = Rect(0, 10),
				["b"] = Rect(10, 20),
				["c"] = Rect(20, 30),
			};
			var intersecting = new HashSet<object> { "b" };
			var intersected = new List<object>();
			var notIntersected = new List<object>();

			// rectangle covers rows a+b: "a" enters, "b" unchanged, "c" stays out
			SelectionIntersectionTracker.ApplyIntersectionChanges(
				itemsPosition, Rect(0, 20), intersecting,
				intersected.Add, notIntersected.Add);

			CollectionAssert.AreEqual(new[] { "a" }, intersected);
			CollectionAssert.AreEqual(new object[] { }, notIntersected);
			Assert.IsTrue(intersecting.SetEquals(new[] { "a", "b" }));
		}

		[TestMethod]
		public void ApplyIntersectionChanges_ShrinkAndGrowInOneMove()
		{
			var itemsPosition = new Dictionary<object, Rectangle>
			{
				["a"] = Rect(0, 10),
				["b"] = Rect(10, 20),
				["c"] = Rect(20, 30),
				["d"] = Rect(30, 40),
			};
			var intersecting = new HashSet<object> { "a", "b", "c" };
			var intersected = new List<object>();
			var notIntersected = new List<object>();

			// window moves down: "a" leaves, "d" enters
			SelectionIntersectionTracker.ApplyIntersectionChanges(
				itemsPosition, Rect(10, 40), intersecting,
				intersected.Add, notIntersected.Add);

			CollectionAssert.AreEqual(new[] { "d" }, intersected);
			CollectionAssert.AreEqual(new[] { "a" }, notIntersected);
			Assert.IsTrue(intersecting.SetEquals(new[] { "b", "c", "d" }));
		}

		[TestMethod]
		public void ApplyIntersectionChanges_EmptyRectangleClearsAll()
		{
			var itemsPosition = new Dictionary<object, Rectangle>
			{
				["a"] = Rect(0, 10),
				["b"] = Rect(10, 20),
			};
			var intersecting = new HashSet<object> { "a", "b" };
			var intersected = new List<object>();
			var notIntersected = new List<object>();

			SelectionIntersectionTracker.ApplyIntersectionChanges(
				itemsPosition, Rectangle.Empty, intersecting,
				intersected.Add, notIntersected.Add);

			Assert.AreEqual(0, intersected.Count);
			CollectionAssert.AreEqual(new[] { "a", "b" }, notIntersected);
			Assert.AreEqual(0, intersecting.Count);
		}

		[TestMethod]
		public void ApplyIntersectionChanges_SameStateIsIdempotent()
		{
			var itemsPosition = new Dictionary<object, Rectangle> { ["a"] = Rect(0, 10) };
			var intersecting = new HashSet<object> { "a" };
			var calls = 0;

			SelectionIntersectionTracker.ApplyIntersectionChanges(
				itemsPosition, Rect(0, 10), intersecting,
				_ => calls++, _ => calls++);

			Assert.AreEqual(0, calls, "no strategy call when nothing changed");
		}

		[TestMethod]
		public void ApplyIntersectionChanges_StaleItemIsRemovedFromTrackingAndSkipped()
		{
			var itemsPosition = new Dictionary<object, Rectangle>
			{
				["stale"] = Rect(0, 10),
				["fresh"] = Rect(10, 20),
			};
			var intersecting = new HashSet<object>();
			var intersected = new List<object>();

			SelectionIntersectionTracker.ApplyIntersectionChanges(
				itemsPosition, Rect(0, 30), intersecting,
				item =>
				{
					if (Equals(item, "stale"))
						throw new ArgumentException("Item is not present in the ItemsSource");
					intersected.Add(item);
				},
				_ => { });

			// the stale item was dropped from both maps and the loop continued
			Assert.IsFalse(itemsPosition.ContainsKey("stale"));
			Assert.IsFalse(intersecting.Contains("stale"));
			CollectionAssert.AreEqual(new[] { "fresh" }, intersected);
			Assert.IsTrue(intersecting.SetEquals(new[] { "fresh" }));
		}

		[TestMethod]
		public void ApplyIntersectionChanges_GrowsSelectionMonotonicallyAcrossMoves()
		{
			// simulates a drag growing downward: full application and incremental
			// bookkeeping must agree on the intersecting set after every move
			var allItems = Enumerable.Range(0, 12).ToDictionary(
				i => (object)i, i => Rect(i * 10, i * 10 + 10));
			var intersecting = new HashSet<object>();

			for (var move = 1; move <= 6; move++)
			{
				var selectionRect = Rect(0, move * 20);
				var expected = allItems.Where(kv => selectionRect.IntersectsWith(kv.Value))
					.Select(kv => kv.Key).ToHashSet();
				var fullApplication = new HashSet<object>(expected);

				SelectionIntersectionTracker.ApplyIntersectionChanges(
					allItems, selectionRect, intersecting,
					_ => { }, _ => { });

				Assert.IsTrue(intersecting.SetEquals(fullApplication), $"diverged at move {move}");
			}
		}
	}
}
