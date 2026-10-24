// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class AppLaunchTrackerTests
	{
		[TestMethod]
		public void PortableLaunchState_RoundTripsWithoutRegistry()
		{
			var path = Path.Combine(Path.GetTempPath(), $"Files.Tests.{Guid.NewGuid():N}", "local-state.json");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			try
			{
				var first = AppLaunchTracker.Track(new JsonLocalStateStore(path), new Version(2, 0));
				Assert.IsTrue(first.IsFirstRun);
				Assert.IsTrue(first.IsAppUpdated);
				Assert.AreEqual(1, first.TotalLaunchCount);

				var updated = AppLaunchTracker.Track(new JsonLocalStateStore(path), new Version(2, 1));
				Assert.IsFalse(updated.IsFirstRun);
				Assert.IsTrue(updated.IsAppUpdated);
				Assert.AreEqual(2, updated.TotalLaunchCount);

				var unchanged = AppLaunchTracker.Track(new JsonLocalStateStore(path), new Version(2, 1));
				Assert.IsFalse(unchanged.IsFirstRun);
				Assert.IsFalse(unchanged.IsAppUpdated);
				Assert.AreEqual(3, unchanged.TotalLaunchCount);
			}
			finally
			{
				Directory.Delete(Path.GetDirectoryName(path)!, true);
			}
		}
	}
}
