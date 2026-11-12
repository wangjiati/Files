// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class StorageHistoryWrapperTests
	{
		[TestMethod]
		public void History_Add150BeyondCap_UndoToBottomThenRedoToTop()
		{
			using var wrapper = new StorageHistoryWrapper();
			var histories = Enumerable.Range(0, 150).Select(i => new HistoryStub(i)).ToArray();
			foreach (var history in histories)
				wrapper.AddHistory(history);

			// Only the newest MaxHistoryCount entries remain; the current entry is the last added.
			Assert.IsFalse(wrapper.CanRedo());
			Assert.IsTrue(wrapper.CanUndo());
			Assert.AreEqual(149, Current(wrapper).Id);

			// Undo must reach the oldest retained entry and stop there: the 50 oldest are gone.
			for (var i = 0; i < StorageHistoryWrapper.MaxHistoryCount; i++)
			{
				Assert.IsTrue(wrapper.CanUndo(), $"Undo must remain available at step {i}");
				Assert.AreEqual(149 - i, Current(wrapper).Id);
				wrapper.DecreaseIndex();
			}
			Assert.IsFalse(wrapper.CanUndo());

			// Redo climbs back to the top of the retained stack in the same order.
			for (var i = 0; i < StorageHistoryWrapper.MaxHistoryCount; i++)
			{
				wrapper.IncreaseIndex();
				Assert.AreEqual(150 - StorageHistoryWrapper.MaxHistoryCount + i, Current(wrapper).Id);
			}
			Assert.IsFalse(wrapper.CanRedo());
			Assert.IsTrue(wrapper.CanUndo());
			Assert.AreEqual(149, Current(wrapper).Id);
		}

		[TestMethod]
		public void History_TruncateMidStack_IndexStaysOnCurrentEntry()
		{
			using var wrapper = new StorageHistoryWrapper();
			for (var i = 0; i < 150; i++)
				wrapper.AddHistory(new HistoryStub(i));

			// Retained stack starts at entry 50; undo partway so index sits mid-stack.
			for (var i = 0; i < 60; i++)
				wrapper.DecreaseIndex();
			Assert.IsTrue(wrapper.CanUndo());
			Assert.IsTrue(wrapper.CanRedo());
			Assert.AreEqual(89, Current(wrapper).Id);

			// Adding after a partial undo drops the redo entries, exactly as without the cap.
			wrapper.AddHistory(new HistoryStub(999));
			Assert.AreEqual(999, Current(wrapper).Id);
			Assert.IsFalse(wrapper.CanRedo());
			Assert.IsTrue(wrapper.CanUndo());

			// Undo walks exactly the 41 retained entries (40 old + the new one) and no further.
			var undoCount = 0;
			while (wrapper.CanUndo())
			{
				wrapper.DecreaseIndex();
				undoCount++;
			}
			Assert.AreEqual(41, undoCount);

			// Redo returns to the entry added mid-stack.
			while (wrapper.CanRedo())
				wrapper.IncreaseIndex();
			Assert.AreEqual(999, Current(wrapper).Id);
			Assert.IsFalse(wrapper.CanRedo());
		}

		[TestMethod]
		public void History_FilledToExactlyCap_NothingDiscarded()
		{
			using var wrapper = new StorageHistoryWrapper();
			var histories = Enumerable.Range(0, StorageHistoryWrapper.MaxHistoryCount).Select(i => new HistoryStub(i)).ToArray();
			foreach (var history in histories)
				wrapper.AddHistory(history);

			// A null history never changes the stack.
			wrapper.AddHistory(null);
			Assert.AreEqual(StorageHistoryWrapper.MaxHistoryCount - 1, Current(wrapper).Id);

			// Filling to exactly the cap keeps every entry reachable.
			for (var i = 0; i < StorageHistoryWrapper.MaxHistoryCount; i++)
			{
				Assert.IsTrue(wrapper.CanUndo());
				Assert.AreEqual(StorageHistoryWrapper.MaxHistoryCount - 1 - i, Current(wrapper).Id);
				wrapper.DecreaseIndex();
			}
			Assert.IsFalse(wrapper.CanUndo());

			// One more entry discards exactly the oldest one; the second-oldest becomes the bottom.
			while (wrapper.CanRedo())
				wrapper.IncreaseIndex();
			wrapper.AddHistory(new HistoryStub(999));
			var undoCount = 0;
			while (wrapper.CanUndo())
			{
				wrapper.DecreaseIndex();
				undoCount++;
			}
			Assert.AreEqual(StorageHistoryWrapper.MaxHistoryCount, undoCount);

			wrapper.IncreaseIndex();
			Assert.AreEqual(1, Current(wrapper).Id);
		}

		private static HistoryStub Current(StorageHistoryWrapper wrapper)
			=> (HistoryStub)wrapper.GetCurrentHistory();

		private sealed class HistoryStub(int id) : IStorageHistory
		{
			public int Id => id;

			public FileOperationType OperationType => FileOperationType.CreateNew;

			public IList<IStorageItemWithPath> Source => [];

			public IList<IStorageItemWithPath>? Destination => null;

			public void Modify(IStorageHistory newHistory) { }

			public void Modify(FileOperationType operationType, IStorageItemWithPath source, IStorageItemWithPath? destination) { }

			public void Modify(FileOperationType operationType, IList<IStorageItemWithPath> source, IList<IStorageItemWithPath>? destination) { }
		}
	}
}
