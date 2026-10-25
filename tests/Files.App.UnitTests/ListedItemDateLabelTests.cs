// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class ListedItemDateLabelTests
	{
		private static readonly DateTimeOffset ModifiedTime = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
		private static readonly DateTimeOffset CreatedTime = new(2023, 6, 7, 8, 9, 10, TimeSpan.Zero);
		private static readonly DateTimeOffset AccessedTime = new(2025, 11, 12, 13, 14, 15, TimeSpan.Zero);
		private static readonly DateTimeOffset DeletedTime = new(2022, 5, 6, 7, 8, 9, TimeSpan.Zero);

		[TestMethod]
		public void RefreshDateLabels_UsesIndependentColumnFormatters()
		{
			var item = CreateItem();

			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified-a"),
				new StubDateTimeFormatter("created-a"),
				new StubDateTimeFormatter("accessed-a"));

			Assert.AreEqual("modified-a", item.ItemDateModified);
			Assert.AreEqual("created-a", item.ItemDateCreated);
			Assert.AreEqual("accessed-a", item.ItemDateAccessed);
			Assert.AreEqual(ModifiedTime, item.ItemDateModifiedReal);
			Assert.AreEqual(CreatedTime, item.ItemDateCreatedReal);
			Assert.AreEqual(AccessedTime, item.ItemDateAccessedReal);
		}

		[TestMethod]
		public void RefreshDateLabels_ChangesLabelsWithoutChangingRawTimes()
		{
			var item = CreateItem();
			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified-a"),
				new StubDateTimeFormatter("created-a"),
				new StubDateTimeFormatter("accessed-a"));

			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified-b"),
				new StubDateTimeFormatter("created-b"),
				new StubDateTimeFormatter("accessed-b"));

			Assert.AreEqual("modified-b", item.ItemDateModified);
			Assert.AreEqual("created-b", item.ItemDateCreated);
			Assert.AreEqual("accessed-b", item.ItemDateAccessed);
			Assert.AreEqual(ModifiedTime, item.ItemDateModifiedReal);
			Assert.AreEqual(CreatedTime, item.ItemDateCreatedReal);
			Assert.AreEqual(AccessedTime, item.ItemDateAccessedReal);
		}

		[TestMethod]
		public void ItemDateModifiedReal_UsesLastAppliedFormatter()
		{
			var item = CreateItem();
			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified-a"),
				new StubDateTimeFormatter("created-a"),
				new StubDateTimeFormatter("accessed-a"));

			var updatedTime = ModifiedTime.AddDays(1);
			item.ItemDateModifiedReal = updatedTime;

			Assert.AreEqual(updatedTime, item.ItemDateModifiedReal);
			Assert.AreEqual("modified-a", item.ItemDateModified);
		}

		[TestMethod]
		public void RefreshDateLabels_DoesNotShareFormattersBetweenContexts()
		{
			var left = CreateItem();
			var right = CreateItem();

			left.RefreshDateLabels(
				new StubDateTimeFormatter("left-modified"),
				new StubDateTimeFormatter("left-created"),
				new StubDateTimeFormatter("left-accessed"));
			right.RefreshDateLabels(
				new StubDateTimeFormatter("right-modified"),
				new StubDateTimeFormatter("right-created"),
				new StubDateTimeFormatter("right-accessed"));

			Assert.AreEqual("left-modified", left.ItemDateModified);
			Assert.AreEqual("right-modified", right.ItemDateModified);

			left.RefreshDateLabels(
				new StubDateTimeFormatter("left-modified-2"),
				new StubDateTimeFormatter("left-created-2"),
				new StubDateTimeFormatter("left-accessed-2"));

			Assert.AreEqual("left-modified-2", left.ItemDateModified);
			Assert.AreEqual("right-modified", right.ItemDateModified);
			Assert.AreEqual("right-created", right.ItemDateCreated);
			Assert.AreEqual("right-accessed", right.ItemDateAccessed);
		}

		[TestMethod]
		public void RefreshDateLabels_KeepsSortKeys()
		{
			var item = CreateItem();
			var modifiedSortKey = SortingHelper.GetSortFunc(SortOption.DateModified);
			var createdSortKey = SortingHelper.GetSortFunc(SortOption.DateCreated);
			var deletedSortKey = SortingHelper.GetSortFunc(SortOption.DateDeleted);

			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified"),
				new StubDateTimeFormatter("created"),
				new StubDateTimeFormatter("accessed"));

			Assert.AreEqual(ModifiedTime, modifiedSortKey(item));
			Assert.AreEqual(CreatedTime, createdSortKey(item));
			Assert.IsNull(deletedSortKey(item));
		}

		[TestMethod]
		public void RecycleBinItem_RefreshDateLabelsUpdatesDeletedLabel()
		{
			var item = new RecycleBinItem(null)
			{
				ItemDateModifiedReal = ModifiedTime,
				ItemDateCreatedReal = CreatedTime,
				ItemDateAccessedReal = AccessedTime,
				ItemDateDeletedReal = DeletedTime,
			};

			item.RefreshDateLabels(
				new StubDateTimeFormatter("modified"),
				new StubDateTimeFormatter("created"),
				new StubDateTimeFormatter("accessed"),
				new StubDateTimeFormatter("deleted"));

			Assert.AreEqual("modified", item.ItemDateModified);
			Assert.AreEqual("created", item.ItemDateCreated);
			Assert.AreEqual("accessed", item.ItemDateAccessed);
			Assert.AreEqual("deleted", item.ItemDateDeleted);
			Assert.AreEqual(DeletedTime, item.ItemDateDeletedReal);
			Assert.AreEqual(DeletedTime, SortingHelper.GetSortFunc(SortOption.DateDeleted)(item));
		}

		private static ListedItem CreateItem()
		{
			return new ListedItem(null)
			{
				ItemDateModifiedReal = ModifiedTime,
				ItemDateCreatedReal = CreatedTime,
				ItemDateAccessedReal = AccessedTime,
			};
		}

		private sealed class StubDateTimeFormatter : IDateTimeFormatter
		{
			private readonly string label;

			public StubDateTimeFormatter(string label)
			{
				this.label = label;
			}

			public string Name => label;

			public string ToShortLabel(DateTimeOffset offset) => label;

			public string ToLongLabel(DateTimeOffset offset) => label;

			public ITimeSpanLabel ToTimeSpanLabel(DateTimeOffset offset, GroupByDateUnit unit)
				=> throw new NotSupportedException();
		}
	}
}
