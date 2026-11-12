// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App;
using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.Settings;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Windows.Storage;

namespace Files.App.UnitTests.Storage
{
	[TestClass]
	public sealed class IncrementalApplyTests
	{
		private static readonly IReadOnlyList<SortOption> sortOptions =
		[
			SortOption.Name,
			SortOption.DateModified,
			SortOption.DateCreated,
			SortOption.Size,
			SortOption.FileType,
			SortOption.Path,
			SortOption.FileTag,
		];

		// Grouped collection updates resolve localized group-count strings, whose static initializer
		// needs IFoldersSettingsService from the process-wide container (configurable exactly once).
		// Whichever class configures the container must therefore register the shared service union.
		[ClassInitialize]
		public static void ClassInitialize(TestContext context)
		{
			if (IsServiceRegistered<IFoldersSettingsService>())
				return;

			var root = Path.Combine(Path.GetTempPath(), "files-incr-tests-" + Guid.NewGuid().ToString("N"));
			AppRuntime.Initialize(DistributionKind.Portable, root);
			Directory.CreateDirectory(AppRuntime.Current.SettingsDirectory);
			ownsRuntimeRoot = true;

			var userSettings = new UserSettingsService();
			try
			{
				Ioc.Default.ConfigureServices(new ServiceCollection()
					.AddSingleton<IUserSettingsService>(userSettings)
					.AddSingleton<ILayoutSettingsService>(new LayoutSettingsService(userSettings.GetSharingContext()))
					.AddSingleton<IFoldersSettingsService>(new FoldersSettingsService(userSettings.GetSharingContext()))
					.BuildServiceProvider());
			}
			catch (InvalidOperationException)
			{
				// Another class already configured the container; grouped tests rely on its registrations
			}
		}

		private static bool ownsRuntimeRoot;

		private static bool IsServiceRegistered<TService>()
		{
			try
			{
				return Ioc.Default.GetService(typeof(TService)) is not null;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		[ClassCleanup]
		public static void ClassCleanup()
		{
			if (!ownsRuntimeRoot || AppRuntime.Current is null)
				return;

			var root = Path.GetFullPath(Path.Combine(AppRuntime.Current.SettingsDirectory, ".."));
			try
			{
				if (Directory.Exists(root))
					Directory.Delete(root, recursive: true);
			}
			catch (IOException)
			{
				// best effort cleanup of the temporary settings root
			}
		}

		// Requirement: the comparison used for in-group placement must agree with the full stable
		// resort (SortingHelper.OrderFileList) for every supported sort option and direction.
		[TestMethod]
		public void Comparison_AgreesWithFullResort_ForAllSortOptions()
		{
			foreach (var option in sortOptions)
			{
				foreach (var direction in new[] { SortDirection.Ascending, SortDirection.Descending })
				{
					foreach (var alongsideFiles in new[] { false, true })
					{
						var items = CreateItems(new Random(20261113));
						var expected = SortingHelper.OrderFileList(items.ToList(), option, direction, alongsideFiles, false).ToList();
						var actual = StableInsertionSort(items.OrderBy(_ => Guid.NewGuid()).ToList(),
							SortingHelper.GetFileListComparison(option, direction, alongsideFiles, false));

						Assert.IsTrue(expected.SequenceEqual(actual),
							$"{option}/{direction}/alongside={alongsideFiles}: incremental order must equal the full resort");
					}
				}
			}
		}

		// Requirement: batches that arrive out of order must land the final list exactly where a full
		// resort would put it, using single-item notifications only (no Reset).
		[TestMethod]
		[DataRow(1)]
		[DataRow(2)]
		[DataRow(3)]
		[DataRow(4)]
		public void OutOfOrderBatches_FinalListEqualsFullResort(int seed)
		{
			var rng = new Random(seed);
			var items = CreateItems(rng, 240);
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);

			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var resets = 0;
			var multiItemNotifications = 0;
			collection.CollectionChanged += (_, e) =>
			{
				if (e.Action == NotifyCollectionChangedAction.Reset)
					resets++;
				if (e.NewItems?.Count is > 1 || e.OldItems?.Count is > 1)
					multiItemNotifications++;
			};

			var target = SortingHelper.OrderFileList(items, SortOption.Name, SortDirection.Ascending, false, false).ToList();

			// First frame: the bulk Reset presentation
			collection.BeginBulkOperation();
			collection.AddRange(target.Take(60).ToList());
			collection.EndBulkOperation();
			Assert.AreEqual(1, resets);

			// Later batches arrive shuffled and in uneven chunks
			var remaining = target.Skip(60).ToList();
			Shuffle(remaining, rng);
			foreach (var batch in Chunk(remaining, 17))
			{
				var nextTarget = SortingHelper.OrderFileList(collection.ToList().Concat(batch).ToList(), SortOption.Name, SortDirection.Ascending, false, false).ToList();
				var summary = collection.TryApplyOrderedChanges(nextTarget, comparison);

				Assert.IsNotNull(summary, "an ordered add-only batch must apply incrementally");
				Assert.IsTrue(nextTarget.SequenceEqual(collection.ToList()), $"seed {seed}: final list must equal the full resort");
			}

			Assert.AreEqual(1, resets, "incremental batches must not raise Reset");
			Assert.AreEqual(0, multiItemNotifications, "incremental batches must raise single-item notifications only");
			Assert.AreEqual(items.Count, collection.Count);
		}

		// Requirement: watcher batches (add/remove) interleaved with enumeration batches must not
		// lose or duplicate items.
		[TestMethod]
		[DataRow(11)]
		[DataRow(12)]
		[DataRow(13)]
		public void InterleavedEnumAndWatchBatches_NoLostOrDuplicatedItems(int seed)
		{
			var rng = new Random(seed);
			var comparison = SortingHelper.GetFileListComparison(SortOption.DateModified, SortDirection.Descending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);

			var source = new List<ListedItem>();
			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var nameCounter = 0;

			for (var round = 0; round < 18; round++)
			{
				// Enumeration batch: a chunk of fresh items
				var enumBatch = Enumerable.Range(0, rng.Next(4, 12))
					.Select(_ => CreateItem($"file{nameCounter++:d4}.txt", size: rng.Next(1000), modified: RandomDate(rng)))
					.ToList();
				source.AddRange(enumBatch);

				// Watcher batch: a few adds and removals of already enumerated items
				if (source.Count > 6)
				{
					var watchAdds = Enumerable.Range(0, rng.Next(0, 3))
						.Select(_ => CreateItem($"file{nameCounter++:d4}.txt", size: rng.Next(1000), modified: RandomDate(rng)))
						.ToList();
					source.AddRange(watchAdds);

					for (var removal = 0; removal < rng.Next(0, 3); removal++)
					{
						var victim = source[rng.Next(source.Count)];
						source.Remove(victim);
					}
				}

				var target = SortingHelper.OrderFileList(source.ToList(), SortOption.DateModified, SortDirection.Descending, false, false).ToList();
				var summary = collection.TryApplyOrderedChanges(target, comparison);

				Assert.IsNotNull(summary, "an ordered interleaved batch must apply incrementally");
				Assert.IsTrue(target.SequenceEqual(collection.ToList()));
				Assert.AreEqual(source.Count, collection.Count);
				Assert.AreEqual(source.Distinct().Count(), collection.ToList().Distinct().Count(), "no duplicated instances");
			}
		}

		// Requirement: group structure changes (new groups, emptying groups, items moving between
		// groups) must end up identical to a full rebuild with ordered groups.
		[TestMethod]
		public void GroupedBatches_NewEmptyingAndMovedGroups_MatchFullRebuild()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);
			Comparison<GroupedCollection<ListedItem>> groupComparison = (x, y) => string.Compare(x.Model.Key, y.Model.Key, StringComparison.Ordinal);

			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>
			{
				ItemGroupKeySelector = item => item.ItemNameRaw?.Length > 0 ? item.ItemNameRaw[..1].ToUpperInvariant() : null,
			};

			var initial = new List<ListedItem>
			{
				CreateItem("apple.txt"),
				CreateItem("avocado.txt"),
				CreateItem("banana.txt"),
				CreateItem("cherry.txt"),
				CreateItem("cocoa.txt"),
				CreateItem("currant.txt"),
			};

			var firstTarget = SortingHelper.OrderFileList(initial, SortOption.Name, SortDirection.Ascending, false, false).ToList();
			collection.BeginBulkOperation();
			collection.AddRange(firstTarget);
			collection.EndBulkOperation();

			// Batch 1: a new mid-alphabet group ("b-new" joins b; "date" creates d; "fig" creates f after... )
			var additions = new List<ListedItem> { CreateItem("blueberry.txt"), CreateItem("date.txt"), CreateItem("fig.txt") };
			var source = initial.Concat(additions).ToList();
			var target = SortingHelper.OrderFileList(source, SortOption.Name, SortDirection.Ascending, false, false).ToList();
			Assert.IsNotNull(collection.TryApplyOrderedChanges(target, comparison, groupComparison));

			// Batch 2: the whole "c" group empties, a new "a" item arrives
			source.RemoveAll(x => x.ItemNameRaw!.StartsWith('c'));
			source.Add(CreateItem("apricot.txt"));
			target = SortingHelper.OrderFileList(source, SortOption.Name, SortDirection.Ascending, false, false).ToList();
			Assert.IsNotNull(collection.TryApplyOrderedChanges(target, comparison, groupComparison));

			// Batch 3: an item is renamed into another group ("date" -> "elderberry")
			var renamed = source.First(x => x.ItemNameRaw == "date.txt");
			renamed.ItemNameRaw = "elderberry.txt";
			target = SortingHelper.OrderFileList(source, SortOption.Name, SortDirection.Ascending, false, false).ToList();
			Assert.IsNotNull(collection.TryApplyOrderedChanges(target, comparison, groupComparison));

			// Reference: a full rebuild from the final list
			var reference = new GroupableBulkConcurrentObservableCollection<ListedItem>
			{
				ItemGroupKeySelector = collection.ItemGroupKeySelector,
			};
			reference.BeginBulkOperation();
			reference.AddRange(target);
			reference.EndBulkOperation();
			reference.ResetGroups();

			Assert.IsTrue(target.SequenceEqual(collection.ToList()), "flat list must match the resort");
			Assert.IsTrue(reference.GroupedCollection!.Select(x => x.Model.Key).SequenceEqual(collection.GroupedCollection!.Select(x => x.Model.Key)),
				"group order must match the rebuild");
			foreach (var referenceGroup in reference.GroupedCollection!)
			{
				var actualGroup = collection.GroupedCollection!.First(x => x.Model.Key == referenceGroup.Model.Key);
				Assert.IsTrue(referenceGroup.SequenceEqual(actualGroup), $"group '{referenceGroup.Model.Key}' contents must match");
			}
			Assert.IsFalse(collection.GroupedCollection!.Any(x => x.Count == 0), "empty groups must be removed");
		}

		// A group key can change without the flat position changing (size sort + name groups).
		[TestMethod]
		public void GroupedBatches_KeyChangeWithoutFlatMove_RegroupsItem()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Size, SortDirection.Ascending, sortDirectoriesAlongsideFiles: true, sortFilesFirst: false);
			Comparison<GroupedCollection<ListedItem>> groupComparison = (x, y) => string.Compare(x.Model.Key, y.Model.Key, StringComparison.Ordinal);

			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>
			{
				ItemGroupKeySelector = item => item.ItemNameRaw?.Length > 0 ? item.ItemNameRaw[..1].ToUpperInvariant() : null,
			};

			var left = CreateItem("alpha.txt", size: 100);
			var right = CreateItem("zeta.txt", size: 900);
			var moving = CreateItem("beta.txt", size: 500);

			var source = new List<ListedItem> { left, moving, right };
			var target = source.ToList();
			collection.BeginBulkOperation();
			collection.AddRange(target);
			collection.EndBulkOperation();
			Assert.IsNotNull(collection.TryApplyOrderedChanges(target, comparison, groupComparison));
			Assert.AreEqual("B", collection.GroupedCollection!.Select(x => x.Model.Key).Skip(1).First());

			// Renaming changes the group key but not the size-sorted order
			moving.ItemNameRaw = "omega.txt";
			var summary = collection.TryApplyOrderedChanges(target, comparison, groupComparison);

			Assert.IsNotNull(summary);
			Assert.AreEqual(1, summary.MovedItems, "the renamed item must be counted as a group move");
			Assert.IsTrue(target.SequenceEqual(collection.ToList()), "the flat order must be untouched");
			Assert.AreEqual("O", collection.GroupedCollection!.Select(x => x.Model.Key).Skip(1).First());
			Assert.IsFalse(collection.GroupedCollection!.Any(x => x.Model.Key == "B"), "the vacated group must be gone");
		}

		[TestMethod]
		public void EmptyDiff_RaisesNoEvents()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);
			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var items = SortingHelper.OrderFileList(CreateItems(new Random(7)), SortOption.Name, SortDirection.Ascending, false, false).ToList();

			collection.BeginBulkOperation();
			collection.AddRange(items);
			collection.EndBulkOperation();

			var events = 0;
			collection.CollectionChanged += (_, _) => events++;

			var summary = collection.TryApplyOrderedChanges(items, comparison);

			Assert.IsNotNull(summary);
			Assert.IsTrue(summary.IsEmpty);
			Assert.AreEqual(0, events, "an empty diff must not touch the collection");
		}

		[TestMethod]
		public void UnorderedTarget_ReturnsNull()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);
			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var items = CreateItems(new Random(8));
			collection.AddRange(items);

			Assert.IsNull(collection.TryApplyOrderedChanges(items, comparison), "an unordered target must fall back to Reset");
		}

		[TestMethod]
		public void WholesaleReorder_ReturnsNull()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);
			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var items = SortingHelper.OrderFileList(CreateItems(new Random(9), 200), SortOption.Name, SortDirection.Ascending, false, false).ToList();

			collection.BeginBulkOperation();
			collection.AddRange(items);
			collection.EndBulkOperation();

			Assert.IsNull(collection.TryApplyOrderedChanges(items.AsEnumerable().Reverse().ToList(), comparison),
				"a wholesale reorder must fall back to Reset");
		}

		// Review round 1: real comparison ties (equal names under Name sort have no tiebreak).
		// New items must keep the rebuild order inside the group, not merely an append position.
		[TestMethod]
		public void TiedComparisonKeys_GroupContentsMatchTheRebuild()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);
			Comparison<GroupedCollection<ListedItem>> groupComparison = (x, y) => string.Compare(x.Model.Key, y.Model.Key, StringComparison.Ordinal);

			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>
			{
				ItemGroupKeySelector = item => item.ItemNameRaw?.Length > 0 ? item.ItemNameRaw[..1].ToUpperInvariant() : null,
			};

			// All items compare equal ("dup.txt" under Name sort): a full-tie group
			var b = CreateItem("dup.txt");
			var c = CreateItem("dup.txt");
			var x = CreateItem("dup.txt");

			// Incoming state as UpdateItemGroup's append can leave it: comparison-sorted with the
			// ties NOT in target order (group [B, C, X] while the target orders X before C)
			collection.BeginBulkOperation();
			collection.AddRange([b, c, x]);
			collection.EndBulkOperation();

			var target = new List<ListedItem> { b, x, c };
			Assert.AreEqual(0, comparison(b, x), "the fixture must be a real comparison tie");
			var summary = collection.TryApplyOrderedChanges(target, comparison, groupComparison);

			Assert.IsNotNull(summary);
			Assert.IsTrue(target.SequenceEqual(collection.ToList()), "the flat list must equal the target exactly");
			var group = collection.GroupedCollection!.Single();
			Assert.IsTrue(target.SequenceEqual(group), "group contents must match the rebuild (tie order included), not the append order");
		}

		// Review round 1: anchor the selection interaction of incremental batches. A bound ListView
		// clears its selection on Reset, removes deselected items on single-item Remove and keeps the
		// rest; this model mirrors those semantics over the raised events.
		[TestMethod]
		public void IncrementalBatches_PreserveSelectionAndDropRemovedItems()
		{
			var comparison = SortingHelper.GetFileListComparison(SortOption.Name, SortDirection.Ascending, sortDirectoriesAlongsideFiles: false, sortFilesFirst: false);

			var collection = new GroupableBulkConcurrentObservableCollection<ListedItem>();
			var a = CreateItem("a.txt");
			var b = CreateItem("b.txt");
			var c = CreateItem("c.txt");
			var d = CreateItem("d.txt");
			var e = CreateItem("e.txt");
			var source = new List<ListedItem> { a, b, c, d, e };

			collection.BeginBulkOperation();
			collection.AddRange(SortingHelper.OrderFileList(source, SortOption.Name, SortDirection.Ascending, false, false).ToList());
			collection.EndBulkOperation();

			var resets = 0;
			selection.Clear();
			selection.UnionWith([b, d, e]);
			collection.CollectionChanged += (_, args) =>
			{
				if (args.Action == NotifyCollectionChangedAction.Reset)
				{
					resets++;
					selection.Clear();
				}
				else if (args.Action == NotifyCollectionChangedAction.Remove && args.OldItems is not null)
				{
					foreach (var removed in args.OldItems)
						selection.Remove((ListedItem)removed!);
				}
			};

			// Watcher batch: d is deleted, f arrives
			var f = CreateItem("f.txt");
			source.Remove(d);
			source.Add(f);
			var target = SortingHelper.OrderFileList(source, SortOption.Name, SortDirection.Ascending, false, false).ToList();

			Assert.IsNotNull(collection.TryApplyOrderedChanges(target, comparison));

			Assert.AreEqual(0, resets, "an incremental batch must not raise Reset (which would clear the control selection)");
			Assert.IsTrue(selection.SetEquals([b, e]), "surviving selected items must stay selected and the removed one must leave the selection");
			Assert.IsTrue(target.SequenceEqual(collection.ToList()));

			// Contrast: the Reset rebuild the fallback path performs clears the modeled selection
			collection.BeginBulkOperation();
			collection.Clear();
			collection.AddRange(target);
			collection.EndBulkOperation();

			Assert.AreEqual(1, resets);
			Assert.AreEqual(0, selection.Count, "the Reset rebuild clears the control selection (previous behavior)");
		}

		private readonly HashSet<ListedItem> selection = new(ReferenceEqualityComparer.Instance);

		private static List<ListedItem> CreateItems(Random rng, int count = 60)
		{
			var items = new List<ListedItem>(count);
			for (var index = 0; index < count; index++)
			{
				var name = $"{(char)('a' + rng.Next(26))}{rng.Next(10000):d4}.txt";
				items.Add(CreateItem(name, filePriority: rng.Next(3) != 0, rng.Next(10_000), RandomDate(rng), RandomDate(rng), "Text file"));
				// First assignment only touches the in-memory property, never the tag database
				if (index % 3 != 2)
					items[index].FileTags = index % 3 == 0 ? ["red"] : ["blue"];
			}
			return items;
		}

		// All items use the folder attribute so Name never consults user settings, keeping the tests
		// independent of the IoC container state. File-priority coverage comes from ShortcutItem, which
		// the sort priority treats as a file while Name still returns the raw name.
		private static ListedItem CreateItem(string name, bool filePriority = false, long size = 0, DateTimeOffset? modified = null, DateTimeOffset? created = null, string? itemType = null)
		{
			var item = filePriority ? new ShortcutItem { IsSymLink = true } : new ListedItem(null);
			item.PrimaryItemAttribute = StorageItemTypes.Folder;
			item.ItemNameRaw = name;
			item.FileSizeBytes = size;
			item.ItemDateModifiedReal = modified ?? default;
			item.ItemDateCreatedReal = created ?? default;
			item.ItemType = itemType ?? "Folder";
			item.ItemPath = "C:\\" + name;
			return item;
		}

		private static DateTimeOffset RandomDate(Random rng)
			=> new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(10_000_000));

		// Emulates a stable sort through repeated upper-bound insertion, the same placement the
		// incremental apply uses inside groups.
		private static List<ListedItem> StableInsertionSort(List<ListedItem> items, Comparison<ListedItem> comparison)
		{
			var result = new List<ListedItem>(items.Count);
			foreach (var item in items)
			{
				var low = 0;
				var high = result.Count;
				while (low < high)
				{
					var mid = (low + high) / 2;
					if (comparison(result[mid], item) <= 0)
						low = mid + 1;
					else
						high = mid;
				}
				result.Insert(low, item);
			}
			return result;
		}

		private static void Shuffle<T>(List<T> list, Random rng)
		{
			for (var index = list.Count - 1; index > 0; index--)
			{
				var swap = rng.Next(index + 1);
				(list[index], list[swap]) = (list[swap], list[index]);
			}
		}

		private static IEnumerable<List<T>> Chunk<T>(List<T> list, int size)
		{
			for (var index = 0; index < list.Count; index += size)
				yield return list.GetRange(index, Math.Min(size, list.Count - index));
		}
	}
}
