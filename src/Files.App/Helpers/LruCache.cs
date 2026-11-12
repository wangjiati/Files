// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	/// <summary>
	/// A bounded thread-safe cache that evicts the least recently used entry when full.
	/// </summary>
	internal sealed class LruCache<TKey, TValue> where TKey : notnull
	{
		private readonly int capacity;
		private readonly Lock gate = new();
		// Most recently used first; nodes map keys to list nodes for O(1) removal.
		private readonly LinkedList<TKey> recencyOrder = [];
		private readonly Dictionary<TKey, LinkedListNode<TKey>> nodes = [];
		private readonly Dictionary<TKey, TValue> values = [];

		public LruCache(int capacity)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
			this.capacity = capacity;
		}

		public int Capacity => capacity;

		public int Count
		{
			get
			{
				lock (gate)
					return values.Count;
			}
		}

		/// <summary>
		/// Reads a value and marks it as most recently used. Cached null values return true.
		/// </summary>
		public bool TryGetValue(TKey key, out TValue value)
		{
			lock (gate)
			{
				if (!nodes.TryGetValue(key, out var node))
				{
					value = default!;
					return false;
				}

				recencyOrder.Remove(node);
				recencyOrder.AddFirst(node);
				value = values[key];
				return true;
			}
		}

		public void Set(TKey key, TValue value)
		{
			lock (gate)
			{
				if (nodes.TryGetValue(key, out var node))
				{
					values[key] = value;
					recencyOrder.Remove(node);
					recencyOrder.AddFirst(node);
					return;
				}

				nodes[key] = recencyOrder.AddFirst(key);
				values[key] = value;
				while (values.Count > capacity)
					RemoveOldest();
			}
		}

		public void EvictOldest(int count)
		{
			lock (gate)
			{
				while (count-- > 0 && recencyOrder.Count > 0)
					RemoveOldest();
			}
		}

		public void Clear()
		{
			lock (gate)
			{
				recencyOrder.Clear();
				nodes.Clear();
				values.Clear();
			}
		}

		private void RemoveOldest()
		{
			var oldestKey = recencyOrder.Last!.Value;
			recencyOrder.RemoveLast();
			nodes.Remove(oldestKey);
			values.Remove(oldestKey);
		}
	}
}
