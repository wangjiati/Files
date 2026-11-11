// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Previews;
using Files.Shared.Helpers;

namespace Files.App.Helpers
{
	public static class AdaptiveLayoutHelpers
	{
		private static ILayoutSettingsService LayoutSettingsService { get; } = Ioc.Default.GetRequiredService<ILayoutSettingsService>();
		private static IContentPageContext ContentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();
		private static IWindowsIniService WindowsIniService { get; } = Ioc.Default.GetRequiredService<IWindowsIniService>();

		private const int PredictionCacheCapacity = 256;

		private static readonly object predictionCacheLock = new();
		private static readonly LinkedList<KeyValuePair<string, FolderLayoutModes>> predictionCacheEntries = new();
		private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, FolderLayoutModes>>> predictionCacheIndex = new(StringComparer.OrdinalIgnoreCase);

		// Predicts the layout from desktop.ini or a small directory sample, so navigation opens in the right layout without a post-enumeration switch
		public static bool TryPredictLayout(string? path, out FolderLayoutModes layout)
		{
			layout = FolderLayoutModes.DetailsView;

			if (path is null)
				return false;

			try
			{
				var root = SystemIO.Path.GetPathRoot(path);
				if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ||
					new SystemIO.DriveInfo(root).DriveType is SystemIO.DriveType.Network or SystemIO.DriveType.NoRootDirectory)
					return false;

				var viewStateSection = WindowsIniService.GetData(SystemIO.Path.Combine(path, "desktop.ini"))
					.FirstOrDefault(x => x.SectionName == "ViewState");
				if (viewStateSection is not null)
				{
					var viewMode = viewStateSection.Parameters.FirstOrDefault(x => x.Key == "Mode").Value;
					layout = viewMode is "Pictures" or "Videos" ? FolderLayoutModes.GridView : FolderLayoutModes.DetailsView;
					return true;
				}

				int total = 0, media = 0;
				foreach (var entry in SystemIO.Directory.EnumerateFileSystemEntries(path))
				{
					if (IsMediaExtension(SystemIO.Path.GetExtension(entry)))
						media++;
					if (++total >= 200)
						break;
				}

				if (total is 0)
					return false;

				layout = 100f * media / total > 60f ? FolderLayoutModes.GridView : FolderLayoutModes.DetailsView;
				return true;
			}
			catch (Exception ex) when (ex is SystemIO.IOException or UnauthorizedAccessException or ArgumentException)
			{
				return false;
			}
		}

		// Serves a prefetched prediction without touching the disk; a miss keeps the default layout
		// and ApplyAdaptativeLayout corrects it after enumeration
		public static bool TryGetCachedPredictLayout(string? path, out FolderLayoutModes layout)
		{
			layout = FolderLayoutModes.DetailsView;

			var key = NormalizeCacheKey(path);
			if (key is null)
				return false;

			lock (predictionCacheLock)
			{
				if (predictionCacheIndex.TryGetValue(key, out var node))
				{
					predictionCacheEntries.Remove(node);
					predictionCacheEntries.AddFirst(node);
					layout = node.Value.Value;
					return true;
				}
			}

			return false;
		}

		// Warms the prediction cache off the UI thread; the caller's token drops the remaining work on navigation
		public static Task PrefetchPredictLayoutsAsync(IEnumerable<string?> paths, CancellationToken ct)
			=> Task.Run(() =>
			{
				try
				{
					foreach (var path in paths)
					{
						ct.ThrowIfCancellationRequested();
						if (TryGetCachedPredictLayout(path, out _))
							continue;
						if (TryPredictLayout(path, out var layout))
							AddCachedPrediction(path, layout);
					}
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					// Navigation moved on; the rest of the prefetch is abandoned.
				}
			}, ct);

		private static void AddCachedPrediction(string? path, FolderLayoutModes layout)
		{
			var key = NormalizeCacheKey(path);
			if (key is null)
				return;

			lock (predictionCacheLock)
			{
				if (predictionCacheIndex.TryGetValue(key, out var node))
				{
					predictionCacheEntries.Remove(node);
					predictionCacheEntries.AddFirst(node);
					node.Value = new(key, layout);
					return;
				}

				predictionCacheIndex.Add(key, predictionCacheEntries.AddFirst(new KeyValuePair<string, FolderLayoutModes>(key, layout)));
				if (predictionCacheIndex.Count > PredictionCacheCapacity)
				{
					var last = predictionCacheEntries.Last!;
					predictionCacheEntries.RemoveLast();
					predictionCacheIndex.Remove(last.Value.Key);
				}
			}
		}

		private static string? NormalizeCacheKey(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			try
			{
				return SystemIO.Path.GetFullPath(path);
			}
			catch (Exception ex) when (ex is ArgumentException or SystemIO.IOException or NotSupportedException or UnauthorizedAccessException)
			{
				return null;
			}
		}

		public static void ApplyAdaptativeLayout(LayoutPreferencesManager folderSettings, IList<ListedItem> filesAndFolders)
		{
			if (LayoutSettingsService.SyncFolderPreferencesAcrossDirectories)
				return;
			if (folderSettings.IsLayoutModeFixed || !folderSettings.IsAdaptiveLayoutEnabled)
				return;

			switch (GetAdaptiveLayout(filesAndFolders))
			{
				case Layouts.Detail when folderSettings.LayoutMode is not FolderLayoutModes.DetailsView:
					folderSettings.ToggleLayoutModeDetailsView(false);
					break;
				case Layouts.Grid when folderSettings.LayoutMode is not FolderLayoutModes.GridView:
					folderSettings.ToggleLayoutModeGridView(false);
					break;
			}
		}

		private static Layouts GetAdaptiveLayout(IList<ListedItem> filesAndFolders)
		{
			var pathLayout = GetPathLayout();
			if (pathLayout is not Layouts.None)
				return pathLayout;

			return GetContentLayout(filesAndFolders);
		}

		private static Layouts GetPathLayout()
		{
			var desktopIni = ContentPageContext.ShellPage?.ShellViewModel?.DesktopIni;
			if (desktopIni is null)
				return Layouts.None;

			var viewStateSection = desktopIni.FirstOrDefault(x => x.SectionName == "ViewState");
			if (viewStateSection is null)
				return Layouts.None;

			var viewMode = viewStateSection.Parameters.FirstOrDefault(x => x.Key == "Mode").Value;

			return viewMode switch
			{
				"Pictures" => Layouts.Grid,
				"Videos" => Layouts.Grid,
				_ => Layouts.Detail,
			};
		}

		private static Layouts GetContentLayout(IList<ListedItem> filesAndFolders)
		{
			int itemCount = filesAndFolders.Count;
			if (filesAndFolders.Count is 0)
				return Layouts.None;

			float mediaPercentage = 100f * filesAndFolders.Count(IsMedia) / itemCount;

			if (mediaPercentage > 60f)
				return Layouts.Grid;
			return Layouts.Detail;

			static bool IsMedia(ListedItem item)
				=> IsMediaExtension(item.FileExtension);
		}

		private static bool IsMediaExtension(string? extension)
			=> !string.IsNullOrEmpty(extension)
			&& (FileExtensionHelpers.IsAudioFile(extension)
			|| FileExtensionHelpers.IsVideoFile(extension)
			|| FileExtensionHelpers.IsImageFile(extension));

		private enum Layouts
		{
			None, // Don't decide. Another function to decide can be called afterwards if available.
			Detail, // Apply the layout Detail.
			Grid, // Apply the layout Grid.
		}
	}
}
