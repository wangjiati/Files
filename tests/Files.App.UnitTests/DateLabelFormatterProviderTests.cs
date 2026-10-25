// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Files.App.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DateLabelFormatterProviderTests
	{
		private static readonly DateTimeOffset ModifiedTime = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
		private static readonly DateTimeOffset CreatedTime = new(2023, 6, 7, 8, 9, 10, TimeSpan.Zero);
		private static readonly DateTimeOffset AccessedTime = new(2025, 11, 12, 13, 14, 15, TimeSpan.Zero);

		[TestMethod]
		public void Apply_SetsLabelsForAllLoadedItemsIncludingTail()
		{
			var resolver = new FakeResolver();
			resolver.SetRule(@"C:\Work", DateTimeColumnKind.Modified, DateTimeFormats.Custom, "M");
			resolver.SetRule(@"C:\Work", DateTimeColumnKind.Created, DateTimeFormats.Custom, "C");
			resolver.SetRule(@"C:\Work", DateTimeColumnKind.Deleted, DateTimeFormats.Custom, "D");
			var provider = CreateProvider(resolver);
			var items = new List<ListedItem>
			{
				CreateItem(ModifiedTime, CreatedTime, AccessedTime),
				CreateItem(ModifiedTime, CreatedTime, AccessedTime),
				CreateItem(ModifiedTime, CreatedTime, AccessedTime),
			};

			provider.Apply(items, provider.Resolve(@"C:\Work"));

			foreach (var item in items)
			{
				Assert.AreEqual("Custom:M", item.ItemDateModified);
				Assert.AreEqual("Custom:C", item.ItemDateCreated);
				Assert.AreEqual("System", item.ItemDateAccessed);
			}

			Assert.AreEqual("Custom:M", items[^1].ItemDateModified);
			Assert.AreEqual("Custom:C", items[^1].ItemDateCreated);
		}

		[TestMethod]
		public void Resolve_TwoWorkingDirectoriesDoNotShareFormatters()
		{
			var resolver = new FakeResolver();
			resolver.SetRule(@"C:\Left", DateTimeColumnKind.Modified, DateTimeFormats.Custom, "left-modified");
			resolver.SetRule(@"C:\Right", DateTimeColumnKind.Modified, DateTimeFormats.Custom, "right-modified");
			var provider = CreateProvider(resolver);
			var leftFormatters = provider.Resolve(@"C:\Left");
			var rightFormatters = provider.Resolve(@"C:\Right");
			var leftItem = CreateItem();
			var rightItem = CreateItem();

			provider.Apply(leftItem, leftFormatters);
			provider.Apply(rightItem, rightFormatters);

			Assert.AreEqual("Custom:left-modified", leftItem.ItemDateModified);
			Assert.AreEqual("Custom:right-modified", rightItem.ItemDateModified);

			var newLeftItem = CreateItem();
			provider.Apply(newLeftItem, leftFormatters);

			Assert.AreEqual("Custom:left-modified", newLeftItem.ItemDateModified);
			Assert.AreEqual("Custom:right-modified", rightItem.ItemDateModified);
			Assert.AreEqual(ModifiedTime, leftItem.ItemDateModifiedReal);
			Assert.AreEqual(ModifiedTime, rightItem.ItemDateModifiedReal);
		}

		[TestMethod]
		public void Resolve_ReResolvesRulesForUpdateDateDisplay()
		{
			var resolver = new FakeResolver();
			resolver.SetRule(@"C:\Work", DateTimeColumnKind.Modified, DateTimeFormats.Custom, "old");
			var provider = CreateProvider(resolver);
			var item = CreateItem();

			provider.Apply(item, provider.Resolve(@"C:\Work"));
			Assert.AreEqual("Custom:old", item.ItemDateModified);

			resolver.SetRule(@"C:\Work", DateTimeColumnKind.Modified, DateTimeFormats.Custom, "new");
			provider.Apply(item, provider.Resolve(@"C:\Work"));

			Assert.AreEqual("Custom:new", item.ItemDateModified);
			Assert.AreEqual(ModifiedTime, item.ItemDateModifiedReal);
		}

		[TestMethod]
		public void Resolve_ReportsApplicationFormatFromColumnRule()
		{
			var resolver = new FakeResolver();
			resolver.SetRule(@"C:\Relative", DateTimeColumnKind.Modified, DateTimeFormats.Application);
			var provider = CreateProvider(resolver);

			Assert.IsTrue(provider.Resolve(@"C:\Relative").UsesApplicationFormat);
			Assert.IsFalse(provider.Resolve(@"C:\Static").UsesApplicationFormat);
		}

		[TestMethod]
		public void Resolve_ReportsApplicationFormatFromGlobalDefault()
		{
			var provider = CreateProvider(new FakeResolver(), DateTimeFormats.Application);

			Assert.IsTrue(provider.Resolve(@"C:\Work").UsesApplicationFormat);
		}

		private static DateLabelFormatterProvider CreateProvider(FakeResolver resolver, DateTimeFormats defaultFormat = DateTimeFormats.System)
			=> new(resolver, new FakeFormatterFactory(), () => defaultFormat, () => null);

		private static ListedItem CreateItem(
			DateTimeOffset? modified = null,
			DateTimeOffset? created = null,
			DateTimeOffset? accessed = null)
		{
			return new ListedItem(null)
			{
				ItemDateModifiedReal = modified ?? ModifiedTime,
				ItemDateCreatedReal = created ?? CreatedTime,
				ItemDateAccessedReal = accessed ?? AccessedTime,
			};
		}

		private sealed class FakeResolver : IDateTimeFormatResolver
		{
			private readonly Dictionary<(string Directory, DateTimeColumnKind Column), DateTimeFormatRule> rules = new();

			public void SetRule(string directory, DateTimeColumnKind column, DateTimeFormats format, string? customPattern = null)
				=> rules[(directory, column)] = new DateTimeFormatRule(column, directory, false, format, customPattern);

			public DateTimeFormatRule Resolve(string directoryPath, DateTimeColumnKind column)
			{
				return rules.TryGetValue((directoryPath, column), out var rule)
					? rule
					: new DateTimeFormatRule(column, null, false, DateTimeFormats.System, null);
			}
		}

		private sealed class FakeFormatterFactory : IDateTimeFormatterFactory
		{
			public IDateTimeFormatter GetDateTimeFormatter(DateTimeFormats dateTimeFormat, string? customPattern = null)
				=> new StubFormatter(customPattern is null ? dateTimeFormat.ToString() : $"{dateTimeFormat}:{customPattern}");
		}

		private sealed class StubFormatter : IDateTimeFormatter
		{
			private readonly string label;

			public StubFormatter(string label)
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
