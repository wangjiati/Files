// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Specialized;

namespace Files.App.Utils.Storage
{
	/// <summary>Summary of a successfully applied ordered-changes diff.</summary>
	public sealed record OrderedChangeSummary(int AddedItems, int RemovedItems, int MovedItems)
	{
		public bool IsEmpty => AddedItems == 0 && RemovedItems == 0 && MovedItems == 0;
	}

	[WinRT.GeneratedWinRTExposedType]
	public sealed partial class GroupableBulkConcurrentObservableCollection<T> : BulkConcurrentObservableCollection<T>
	{
		// When 'GroupOption' is set to 'None' or when a folder is opened, 'GroupedCollection' is assigned 'null' by 'ItemGroupKeySelector'
		public BulkConcurrentObservableCollection<GroupedCollection<T>>? GroupedCollection { get; private set; }

		public bool IsGrouped => ItemGroupKeySelector is not null;

		private Func<T, string?>? itemGroupKeySelector;

		public Func<T, string?>? ItemGroupKeySelector
		{
			get => itemGroupKeySelector;
			set
			{
				itemGroupKeySelector = value;
				if (value is not null)
					GroupedCollection ??= [];
				else
					GroupedCollection = null;
			}
		}

		// Set while TryApplyOrderedChanges maintains the groups itself; defers UpdateGroups
		private bool isApplyingOrderedChanges;

		/// <summary>
		/// Applies the difference between the current items and <paramref name="target"/> using single-item
		/// change notifications only, without a Reset. Items end up at their exact position in
		/// <paramref name="target"/> (the flat list, each group's contents, and the group order all match a
		/// Clear+AddRange rebuild followed by ordering), so already realized containers are preserved.
		/// Must be called on the thread that owns the collection.
		/// </summary>
		/// <param name="target">The desired contents and order. Must be ordered by <paramref name="comparison"/>.</param>
		/// <param name="comparison">A comparison consistent with the ordering of <paramref name="target"/>.</param>
		/// <param name="groupComparison">
		/// A comparison matching the group ordering semantics, used to place new groups. Null places new
		/// groups at the end (the collection must then not be grouped).
		/// </param>
		/// <returns>
		/// The applied diff summary, or null when the change is too large for incremental application or an
		/// ordering invariant does not hold; the caller must then fall back to a Reset.
		/// </returns>
		public OrderedChangeSummary? TryApplyOrderedChanges(IList<T> target, Comparison<T> comparison, Comparison<GroupedCollection<T>>? groupComparison = null)
		{
			if (!IsOrdered(target, comparison))
				return null;

			var groupsByKey = new Dictionary<string, GroupedCollection<T>>(StringComparer.Ordinal);
			if (IsGrouped)
			{
				if (GroupedCollection is null)
					return null;

				foreach (var group in GroupedCollection.ToList())
				{
					if (group.Count == 0 || group.Model.Key is null)
						return null;
					if (!IsOrdered(group.ToList(), comparison))
						return null;
					groupsByKey[group.Model.Key] = group;
				}

				if (groupComparison is not null && !IsGroupOrderValid(groupComparison))
					return null;
			}

			var current = Items.ToList();
			// Keyed by the item reference: the diff tracks instances, not values
			var targetIndexOf = new Dictionary<object, int>(target.Count, ReferenceEqualityComparer.Instance);
			for (var index = 0; index < target.Count; index++)
				targetIndexOf.TryAdd(target[index]!, index);

			// Candidates are items kept from the current list; the longest ascending subsequence of their
			// target indices can stay in place, everything else is removed and reinserted at its exact slot.
			var candidateItems = new List<T>(current.Count);
			var candidateIndices = new List<int>(current.Count);
			var removedItems = new List<T>();
			var removedCount = 0;
			var movedCount = 0;

			foreach (var item in current)
			{
				if (item is null || !targetIndexOf.TryGetValue(item, out var targetIndex))
				{
					if (item is not null)
					{
						removedItems.Add(item);
						removedCount++;
					}
					continue;
				}

				// An item whose group key changed must move to another group even when its position holds
				if (IsGrouped
					&& item is IGroupableItem groupable
					&& !string.Equals(GetGroupKeyForItem(item), groupable.Key, StringComparison.Ordinal))
				{
					movedCount++;
					removedItems.Add(item);
					continue;
				}

				candidateItems.Add(item);
				candidateIndices.Add(targetIndex);
			}

			var kept = GetLongestAscendingMask(candidateIndices);
			var keptCount = 0;
			for (var index = 0; index < candidateItems.Count; index++)
			{
				if (kept[index])
					keptCount++;
				else
				{
					removedItems.Add(candidateItems[index]);
					movedCount++;
				}
			}

			var addedCount = target.Count - keptCount;

			// A Reset is cheaper than storming the list with per-item notifications
			if (removedCount > 128 && (long)removedCount * 4 > current.Count)
				return null;
			if (movedCount > 32 && (long)movedCount * 4 > current.Count)
				return null;
			if (addedCount > 25_000 || (long)addedCount * Math.Max(current.Count, target.Count) > 50_000_000)
				return null;

			if (removedItems.Count == 0 && addedCount == 0)
				return new OrderedChangeSummary(0, 0, 0);

			var keptItems = new HashSet<object>(keptCount, ReferenceEqualityComparer.Instance);
			for (var index = 0; index < candidateItems.Count; index++)
			{
				if (kept[index])
					keptItems.Add(candidateItems[index]!);
			}

			isApplyingOrderedChanges = true;
			try
			{
				foreach (var item in removedItems)
				{
					Remove(item);
					RemoveFromGroup(item, groupsByKey);
				}

				for (var index = 0; index < target.Count; index++)
				{
					var item = target[index];
					if (item is null || keptItems.Contains(item))
						continue;

					Insert(index, item);
					InsertIntoGroup(item, comparison, groupComparison, groupsByKey);
				}

				if (IsGrouped && GroupedCollection is not null)
					GroupedCollection.IsSorted = true;
			}
			finally
			{
				isApplyingOrderedChanges = false;
			}

			return new OrderedChangeSummary(addedCount, removedCount, movedCount);
		}

		private static bool IsOrdered(IList<T> items, Comparison<T> comparison)
		{
			for (var index = 1; index < items.Count; index++)
			{
				if (comparison(items[index - 1], items[index]) > 0)
					return false;
			}
			return true;
		}

		private bool IsGroupOrderValid(Comparison<GroupedCollection<T>> groupComparison)
		{
			var groups = GroupedCollection!.ToList();
			for (var index = 1; index < groups.Count; index++)
			{
				if (groupComparison(groups[index - 1], groups[index]) > 0)
					return false;
			}
			return true;
		}

		/// <summary>Returns, per entry, whether it is on the longest strictly ascending subsequence.</summary>
		private static bool[] GetLongestAscendingMask(List<int> values)
		{
			var count = values.Count;
			var mask = new bool[count];
			if (count == 0)
				return mask;

			// Patience sorting: tails[n] = index into values of the smallest tail of an ascending run of length n+1
			var tails = new int[count];
			var previous = new int[count];
			var length = 0;

			for (var index = 0; index < count; index++)
			{
				var value = values[index];
				var low = 0;
				var high = length;
				while (low < high)
				{
					var mid = (low + high) / 2;
					if (values[tails[mid]] < value)
						low = mid + 1;
					else
						high = mid;
				}

				previous[index] = low > 0 ? tails[low - 1] : -1;
				tails[low] = index;
				if (low == length)
					length++;
			}

			for (var walk = tails[length - 1]; walk >= 0; walk = previous[walk])
				mask[walk] = true;

			return mask;
		}

		private void RemoveFromGroup(T item, Dictionary<string, GroupedCollection<T>> groupsByKey)
		{
			if (GroupedCollection is null)
				return;

			// The recorded key names the group the item is displayed in; the current key may already differ
			var group = (item as IGroupableItem)?.Key is { } recordedKey && groupsByKey.TryGetValue(recordedKey, out var byKey)
				? byKey
				: GroupedCollection.ToList().FirstOrDefault(x => x.Contains(item));

			if (group is null)
				return;

			group.Remove(item);
			if (group.Count == 0)
			{
				GroupedCollection.Remove(group);
				if (group.Model.Key is not null)
					groupsByKey.Remove(group.Model.Key);
			}
		}

		private void InsertIntoGroup(T item, Comparison<T> comparison, Comparison<GroupedCollection<T>>? groupComparison, Dictionary<string, GroupedCollection<T>> groupsByKey)
		{
			if (GroupedCollection is null)
				return;

			var key = GetGroupKeyForItem(item);
			if (key is null)
				return;

			if (item is IGroupableItem groupable)
				groupable.Key = key;

			if (groupsByKey.TryGetValue(key, out var group))
			{
				group.Insert(GetUpperBound(group, item, comparison), item);
				return;
			}

			group = new GroupedCollection<T>(key)
			{
				item
			};

			group.GetExtendedGroupHeaderInfo = GetExtendedGroupHeaderInfo;
			if (GetGroupHeaderInfo is not null)
				GetGroupHeaderInfo.Invoke(group);

			groupsByKey[key] = group;

			if (groupComparison is not null && GroupedCollection.Count > 0)
				GroupedCollection.Insert(GetGroupUpperBound(groupComparison, group), group);
			else
				GroupedCollection.Add(group);
		}

		private static int GetUpperBound(IList<T> items, T item, Comparison<T> comparison)
		{
			// Insert after every entry that compares equal, matching stable-sort append semantics
			var low = 0;
			var high = items.Count;
			while (low < high)
			{
				var mid = (low + high) / 2;
				if (comparison(items[mid], item) <= 0)
					low = mid + 1;
				else
					high = mid;
			}
			return low;
		}

		private int GetGroupUpperBound(Comparison<GroupedCollection<T>> groupComparison, GroupedCollection<T> group)
		{
			var low = 0;
			var high = GroupedCollection!.Count;
			while (low < high)
			{
				var mid = (low + high) / 2;
				if (groupComparison(GroupedCollection[mid], group) <= 0)
					low = mid + 1;
				else
					high = mid;
			}
			return low;
		}

		public Action<GroupedCollection<T>>? GetGroupHeaderInfo { get; set; }
		public Action<GroupedCollection<T>>? GetExtendedGroupHeaderInfo { get; set; }

		protected override void OnBulkOperationStarted()
		{
			GroupedCollection?.ForEach(gp => gp.BeginBulkOperation());
			GroupedCollection?.BeginBulkOperation();
		}

		protected override void OnBulkOperationEnded()
		{
			GroupedCollection?.ForEach(gp => gp.EndBulkOperation());
			GroupedCollection?.EndBulkOperation();
		}

		protected override void OnCleared()
		{
			GroupedCollection?.Clear();
		}

		protected override void UpdateGroups(NotifyCollectionChangedEventArgs e)
		{
			// TryApplyOrderedChanges maintains the groups itself with exact sorted placement
			if (isApplyingOrderedChanges)
				return;

			if (IsGrouped)
			{
				if (e.Action is NotifyCollectionChangedAction.Move)
				{
					ResetGroups();
					return;
				}

				if (e.NewItems is not null)
					AddItemsToGroup(e.NewItems.Cast<T>());

				if (e.OldItems is not null)
					RemoveItemsFromGroup(e.OldItems.Cast<T>());
			}
		}

		public void ResetGroups(CancellationToken token = default)
		{
			if (!IsGrouped)
				return;

			// Prevents any unwanted errors caused by bindings updating
			GroupedCollection?.ForEach(x => x.Model.PausePropertyChangedNotifications());
			GroupedCollection?.Clear();
			AddItemsToGroup(Items, token);
		}

		private void AddItemsToGroup(IEnumerable<T> items, CancellationToken token = default)
		{
			if (GroupedCollection is null)
				return;

			var groupsByKey = new Dictionary<string, GroupedCollection<T>>(StringComparer.Ordinal);
			foreach (var group in GroupedCollection)
			{
				if (group.Model.Key is not null)
					groupsByKey.TryAdd(group.Model.Key, group);
			}

			foreach (var item in items)
			{
				if (token.IsCancellationRequested)
					return;

				var key = GetGroupKeyForItem(item);
				if (key is null)
					continue;

				groupsByKey.TryGetValue(key, out var gp);
				if (item is IGroupableItem groupable)
					groupable.Key = key;

				if (gp is not null)
				{
					gp.Add(item);
					gp.IsSorted = false;
				}
				else
				{
					var group = new GroupedCollection<T>(key)
					{
						item
					};

					group.GetExtendedGroupHeaderInfo = GetExtendedGroupHeaderInfo;
					if (GetGroupHeaderInfo is not null)
						GetGroupHeaderInfo.Invoke(group);

					GroupedCollection?.Add(group);
					GroupedCollection!.IsSorted = false;
					groupsByKey[key] = group;
				}
			}
		}

		private void RemoveItemsFromGroup(IEnumerable<T> items)
		{
			foreach (var item in items)
			{
				var key = GetGroupKeyForItem(item);

				var group = GroupedCollection?.Where(x => x.Model.Key == key).FirstOrDefault();
				if (group is not null)
				{
					group.Remove(item);
					if (group.Count == 0)
						GroupedCollection?.Remove(group);
				}
			}
		}

		/// <summary>
		/// Moves the item to its correct group if its group key has changed, e.g. after its properties were updated.
		/// </summary>
		/// <returns>True if the item was moved to another group; otherwise, false.</returns>
		public bool UpdateItemGroup(T item)
		{
			if (!IsGrouped || item is not IGroupableItem groupable)
				return false;

			var key = GetGroupKeyForItem(item);
			if (key is null || key == groupable.Key)
				return false;

			// Only move items that are currently displayed in their recorded group
			var oldGroup = GroupedCollection?.FirstOrDefault(x => x.Model.Key == groupable.Key);
			if (oldGroup is null || !oldGroup.Remove(item))
				return false;

			if (oldGroup.Count == 0)
				GroupedCollection?.Remove(oldGroup);

			AddItemsToGroup((T[])[item]);

			return true;
		}

		private string? GetGroupKeyForItem(T item)
		{
			return ItemGroupKeySelector?.Invoke(item);
		}
	}
}
