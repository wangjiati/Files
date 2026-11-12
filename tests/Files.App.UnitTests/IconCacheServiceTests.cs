// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class IconCacheServiceTests
	{
		[TestMethod]
		public void IconCache_BothCachesAreLruBoundedAt1024()
		{
			// GetIconAsync needs App.AppModel and the app server, neither of which exists in
			// the unit-test host, so the boundary is verified on the service's own caches.
			var service = new IconCacheService();
			var byteCache = GetCache<byte[]?>(service, "_cache");
			var imageCache = GetCache<BitmapImage>(service, "_imageCache");

			Assert.AreEqual(1024, byteCache.Capacity);
			Assert.AreEqual(1024, imageCache.Capacity);

			// Filling beyond the capacity evicts the oldest icons but keeps cached misses:
			// a null icon result must stay cached so the server is not queried again.
			for (var i = 0; i <= byteCache.Capacity; i++)
				byteCache.Set("key" + i, null);
			Assert.AreEqual(byteCache.Capacity, byteCache.Count);
			Assert.IsFalse(byteCache.TryGetValue("key0", out _));
			Assert.IsTrue(byteCache.TryGetValue("key1", out var cached));
			Assert.IsNull(cached);

			// Clear keeps its signature and empties both caches.
			service.Clear();
			Assert.AreEqual(0, byteCache.Count);
			Assert.AreEqual(0, imageCache.Count);
		}

		private static LruCache<string, TValue> GetCache<TValue>(IconCacheService service, string fieldName)
		{
			var field = typeof(IconCacheService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
			return (LruCache<string, TValue>)field.GetValue(service)!;
		}
	}
}
