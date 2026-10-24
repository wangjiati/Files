// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App
{
	internal readonly record struct AppLaunchState(bool IsAppUpdated, bool IsFirstRun, long TotalLaunchCount);

	internal static class AppLaunchTracker
	{
		private const string LastLaunchVersionKey = "LastLaunchVersion";
		private const string TotalLaunchCountKey = "TotalLaunchCount";

		public static AppLaunchState Track(ILocalStateStore store, Version version)
		{
			var lastLaunchVersion = store.GetString(LastLaunchVersionKey);
			var isFirstRun = lastLaunchVersion is null;
			var isAppUpdated = isFirstRun || !string.Equals(lastLaunchVersion, version.ToString(), StringComparison.Ordinal);
			var totalLaunchCount = long.TryParse(store.GetString(TotalLaunchCountKey), out var previousCount) ? previousCount + 1 : 1;

			store.SetString(LastLaunchVersionKey, version.ToString());
			store.SetString(TotalLaunchCountKey, totalLaunchCount.ToString());
			return new(isAppUpdated, isFirstRun, totalLaunchCount);
		}
	}
}
