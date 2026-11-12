// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Models;
using Files.App.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Selection
{
	[TestClass]
	public sealed class ItemManipulationModelTests
	{
		[TestMethod]
		public void SetSelectedItems_RaisesSingleBatchEventWithoutPerItemEvents()
		{
			var model = new ItemManipulationModel();
			var items = new List<ListedItem> { CreateItem("a"), CreateItem("b"), CreateItem("c") };

			var batchEvents = new List<IList<ListedItem>>();
			var clearEvents = 0;
			var addEvents = new List<ListedItem>();
			model.SetSelectedItemsInvoked += (sender, received) => batchEvents.Add(received);
			model.ClearSelectionInvoked += (sender, e) => clearEvents++;
			model.AddSelectedItemInvoked += (sender, item) => addEvents.Add(item);

			model.SetSelectedItems(items);

			Assert.AreEqual(1, batchEvents.Count);
			Assert.IsTrue(items.SequenceEqual(batchEvents[0]), "The batch event must carry the requested items in order");
			Assert.AreEqual(0, clearEvents, "The batch path must not fall back to a per-item clear");
			Assert.AreEqual(0, addEvents.Count, "The batch path must not raise per-item add events");
		}

		[TestMethod]
		public void SetSelectedItem_AndAddSelectedItems_KeepPerItemSemantics()
		{
			var model = new ItemManipulationModel();
			var first = CreateItem("a");
			var second = CreateItem("b");

			var clearEvents = 0;
			var addEvents = new List<ListedItem>();
			var removeEvents = new List<ListedItem>();
			model.ClearSelectionInvoked += (sender, e) => clearEvents++;
			model.AddSelectedItemInvoked += (sender, item) => addEvents.Add(item);
			model.RemoveSelectedItemInvoked += (sender, item) => removeEvents.Add(item);

			model.SetSelectedItem(first);
			Assert.AreEqual(1, clearEvents);
			CollectionAssert.AreEqual(new[] { first }, addEvents.ToArray());

			model.AddSelectedItems([second]);
			CollectionAssert.AreEqual(new[] { first, second }, addEvents.ToArray());

			model.RemoveSelectedItems([first]);
			CollectionAssert.AreEqual(new[] { first }, removeEvents.ToArray());
		}

		[TestMethod]
		public void SetSelectedItems_WithoutBatchSubscriber_IsNoOp()
		{
			// Layouts that do not hook the batch event (for example the columns host page)
			// must observe the same no-op behavior the per-item events already produced.
			var model = new ItemManipulationModel();
			var clearEvents = 0;
			var addEvents = 0;
			model.ClearSelectionInvoked += (sender, e) => clearEvents++;
			model.AddSelectedItemInvoked += (sender, item) => addEvents++;

			model.SetSelectedItems([CreateItem("a"), CreateItem("b")]);

			Assert.AreEqual(0, clearEvents);
			Assert.AreEqual(0, addEvents);
		}

		private static ListedItem CreateItem(string name) => new(null) { ItemNameRaw = name };
	}
}
