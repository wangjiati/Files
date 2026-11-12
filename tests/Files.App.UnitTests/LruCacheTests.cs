// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class LruCacheTests
	{
		[TestMethod]
		public void LruCache_OverCapacityEvictsLeastRecentlyUsed()
		{
			var cache = new LruCache<string, int>(3);
			cache.Set("a", 1);
			cache.Set("b", 2);
			cache.Set("c", 3);
			Assert.AreEqual(3, cache.Count);

			// Exceeding the capacity evicts only the oldest entry.
			cache.Set("d", 4);
			Assert.AreEqual(3, cache.Count);
			Assert.IsFalse(cache.TryGetValue("a", out _));

			// Reads refresh recency: "b" survives while "c" ages out.
			Assert.IsTrue(cache.TryGetValue("b", out var b));
			Assert.AreEqual(2, b);
			cache.Set("e", 5);
			Assert.IsFalse(cache.TryGetValue("c", out _));
			Assert.IsTrue(cache.TryGetValue("b", out _));
			Assert.IsTrue(cache.TryGetValue("d", out _));
			Assert.IsTrue(cache.TryGetValue("e", out _));
		}

		[TestMethod]
		public void LruCache_UpdateExistingRefreshesValueAndRecency()
		{
			var cache = new LruCache<string, int>(2);
			cache.Set("a", 1);
			cache.Set("b", 2);

			// Re-setting "a" makes it the most recently used entry with the new value.
			cache.Set("a", 10);
			cache.Set("c", 3);

			Assert.AreEqual(2, cache.Count);
			Assert.IsFalse(cache.TryGetValue("b", out _));
			Assert.IsTrue(cache.TryGetValue("a", out var a));
			Assert.AreEqual(10, a);
		}

		[TestMethod]
		public void LruCache_EvictOldestDropsRequestedNumberOfOldestEntries()
		{
			var cache = new LruCache<string, int>(10);
			for (var i = 0; i < 10; i++)
				cache.Set("key" + i, i);

			// CleanAsync drops the oldest half of a session cache.
			cache.EvictOldest(10 / 2);
			Assert.AreEqual(5, cache.Count);
			Assert.IsFalse(cache.TryGetValue("key0", out _));
			Assert.IsFalse(cache.TryGetValue("key4", out _));
			Assert.IsTrue(cache.TryGetValue("key5", out _));
			Assert.IsTrue(cache.TryGetValue("key9", out _));

			// Evicting more entries than present stays within the cache size.
			cache.EvictOldest(100);
			Assert.AreEqual(0, cache.Count);
		}

		[TestMethod]
		public void LruCache_ConcurrentAccessStaysBoundedAndConsistent()
		{
			var cache = new LruCache<int, int>(64);
			for (var i = 0; i < 64; i++)
				cache.Set(i, i);

			// No eviction can happen (64 distinct keys at capacity 64): every read must
			// observe a consistent node/value pair, proving the lock keeps structures in sync.
			Parallel.For(0, 10_000, i =>
			{
				var key = i % 64;
				cache.Set(key, key);
				Assert.IsTrue(cache.TryGetValue(key, out var value));
				Assert.AreEqual(key, value);
			});
			Assert.AreEqual(64, cache.Count);
		}
	}
}
