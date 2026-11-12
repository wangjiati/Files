// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Services.SizeProvider;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class CachedSizeProviderTests
	{
		[TestMethod]
		public async Task SizeProvider_CleanAndClearEvictCachedEntries()
		{
			using var directory = new TemporaryDirectory();
			var root = directory.Path;
			var payloadA = new byte[300];
			var payloadB = new byte[70];
			await File.WriteAllBytesAsync(Path.Combine(root, "a.bin"), payloadA, CancellationToken.None);
			await File.WriteAllBytesAsync(Path.Combine(root, "b.bin"), payloadB, CancellationToken.None);

			var provider = new CachedSizeProvider();
			await provider.UpdateAsync(root, CancellationToken.None);

			// Cached entries: the scanned root plus the empty path written by file iterations
			// at level <= 3 (existing behavior). The empty path is always inserted first, and
			// reads refresh recency, so only the root is probed before CleanAsync runs.
			var cache = GetSizesCache(provider);
			Assert.AreEqual(2, cache.Count);
			Assert.IsTrue(provider.TryGetSize(root, out var size));
			Assert.AreEqual((ulong)(payloadA.Length + payloadB.Length), size);

			// CleanAsync drops the oldest half; the freshly read root entry survives.
			await provider.CleanAsync();
			Assert.AreEqual(1, cache.Count);
			Assert.IsFalse(provider.TryGetSize(string.Empty, out _), "The oldest half must be evicted");
			Assert.IsTrue(provider.TryGetSize(root, out var kept));
			Assert.AreEqual((ulong)(payloadA.Length + payloadB.Length), kept);

			// ClearAsync removes everything; callers recalculate on the next miss.
			await provider.ClearAsync();
			Assert.AreEqual(0, cache.Count);
			Assert.IsFalse(provider.TryGetSize(root, out _));
		}

		[TestMethod]
		public void SizeProvider_CacheIsBoundedAtCapacityAndEvictedPathsReportMisses()
		{
			var provider = new CachedSizeProvider();
			var cache = GetSizesCache(provider);

			Assert.AreEqual(4096, cache.Capacity);
			for (var i = 0; i <= cache.Capacity; i++)
				cache.Set("path" + i, (ulong)i);

			// The oldest path was evicted: TryGetSize reports a miss so callers recalculate.
			Assert.IsFalse(provider.TryGetSize("path0", out _));
			Assert.IsTrue(provider.TryGetSize("path" + cache.Capacity, out var latest));
			Assert.AreEqual((ulong)cache.Capacity, latest);
		}

		private static LruCache<string, ulong> GetSizesCache(CachedSizeProvider provider)
		{
			var field = typeof(CachedSizeProvider).GetField("sizes", BindingFlags.NonPublic | BindingFlags.Instance)!;
			return (LruCache<string, ulong>)field.GetValue(provider)!;
		}

		private sealed class TemporaryDirectory : IDisposable
		{
			public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Files.Tests.{Guid.NewGuid():N}");

			public TemporaryDirectory()
			{
				Directory.CreateDirectory(Path);
			}

			public void Dispose()
			{
				Directory.Delete(Path, true);
			}
		}
	}
}
