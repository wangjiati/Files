// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Shell;

namespace Files.App.Server.Core;

internal sealed class SystemDataSource : ISystemDataSource
{
	private readonly ShellNewRegistryMonitor shellNewRegistry = new();
	private readonly RegistryChangeMonitor wslRegistry = new(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Lxss");
	private readonly FileStampMonitor librariesMonitor;
	private readonly FileStampMonitor pinnedFoldersMonitor;
	private readonly ILogger logger;

	internal SystemDataSource(ILogger logger)
	{
		this.logger = logger;
		librariesMonitor = new(() => Directory.Exists(ShellLibraryItem.LibrariesPath)
			? Directory.EnumerateFiles(ShellLibraryItem.LibrariesPath, "*.library-ms") : Array.Empty<string>());
		pinnedFoldersMonitor = new(() => (string[])[Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent\AutomaticDestinations\f01b4d95cf55d32a.automaticDestinations-ms")]);
	}

	public async Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync(string cacheDirectory) =>
		await ShellNewMenuHelper.GetNewContextMenuEntries(cacheDirectory);

	public async Task<ShellNewEntry?> GetShellNewEntryAsync(string extension, string cacheDirectory) =>
		await ShellNewMenuHelper.GetNewContextMenuEntryForType(extension, cacheDirectory);

	public Task<IReadOnlyList<string>> GetWslDistributionsAsync()
	{
		using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
		var names = new List<string>();
		foreach (var subKeyName in key?.GetSubKeyNames() ?? [])
		{
			using var distro = key!.OpenSubKey(subKeyName);
			if (distro?.GetValue("DistributionName") is string { Length: > 0 } name)
				names.Add(name);
		}
		return Task.FromResult<IReadOnlyList<string>>(names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
	}

	public Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync() => STATask.Run<IReadOnlyList<ShellLibraryItem>>(() =>
	{
		var result = new List<ShellLibraryItem>();
		if (Directory.Exists(ShellLibraryItem.LibrariesPath))
		{
			foreach (var file in Directory.EnumerateFiles(ShellLibraryItem.LibrariesPath, "*.library-ms"))
			{
				using var item = ShellItem.Open(file);
				using var library = new ShellLibraryEx(item.IShellItem, true);
				var snapshot = new ShellLibraryItem
				{
					FullPath = file,
					AbsolutePath = library.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING),
					RelativePath = library.GetDisplayName(SIGDN.SIGDN_PARENTRELATIVEPARSING),
					DisplayName = library.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY),
					IsPinned = library.PinnedToNavigationPane,
				};
				if (library.Folders.Count > 0)
				{
					using var defaultFolder = SafetyExtensions.IgnoreExceptions(() => library.DefaultSaveFolder);
					snapshot.DefaultSaveFolder = defaultFolder?.FileSystemPath ?? string.Empty;
					snapshot.Folders = library.Folders.Select(folder => folder.FileSystemPath).OfType<string>().ToArray();
				}
				result.Add(snapshot);
			}
		}
		return result.AsReadOnly();
	}, logger);

	public Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync() => STATask.Run<IReadOnlyList<PinnedFolder>>(() =>
	{
		var result = new List<PinnedFolder>();
		using var folder = new ShellFolder("shell:::{3936e9e4-d92c-4eee-a85a-bc16d5ea0819}");
		foreach (var item in folder)
		{
			try
			{
				using (item)
				{
					var isFolder = item is Files.Storage.ShellLink link
						? link.StoredTargetIsFolder() ?? link.IsTargetFolder(Environment.ExpandEnvironmentVariables(link.TargetPath))
						: item.IsFolder && !item.IsStream;
					if (!isFolder)
						continue;

					var parsingPath = item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING) ?? item.FileSystemPath;
					if (string.IsNullOrEmpty(parsingPath) || !Path.IsPathRooted(parsingPath))
					{
						parsingPath = parsingPath switch
						{
							"::{645FF040-5081-101B-9F08-00AA002F954E}" => "Shell:RecycleBinFolder",
							"::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}" or "::{208D2C60-3AEA-1069-A2D7-08002B30309D}" => "Shell:NetworkPlacesFolder",
							"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" => "Shell:MyComputerFolder",
							{ } path when path.StartsWith("::{031E4825-7B94-4DC3-B131-E946B44C8DD5}\\", StringComparison.OrdinalIgnoreCase)
								=> Path.Combine(ShellLibraryItem.LibrariesPath, Path.GetFileName(path)),
							_ => $@"\\SHELL\{string.Join("\\", item.PIDL.Select(segment => Convert.ToBase64String(segment.GetBytes()).Replace('/', '_')))}"
						};
					}
					result.Add(new PinnedFolder(
						Path.IsPathRooted(item.Name) ? item.Name! : parsingPath,
						item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY) ?? string.Empty,
						item.PIDL.GetBytes(),
						(bool?)SafetyExtensions.IgnoreExceptions(() => item.Properties["System.Home.IsPinned"], logger) ?? false));
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
			{
				logger.LogWarning(ex, "Could not read a pinned folder.");
			}
		}
		return result.AsReadOnly();
	}, logger);

	public bool HasLibrariesChanged() => librariesMonitor.HasChanged();
	public bool HasPinnedFoldersChanged() => pinnedFoldersMonitor.HasChanged();
	public bool HasShellNewChanged() => shellNewRegistry.HasChanged();
	public bool HasWslChanged() => wslRegistry.HasChanged();
}

internal sealed class FileStampMonitor(Func<IEnumerable<string>> getPaths)
{
	private string? previous;

	internal bool HasChanged()
	{
		try
		{
			var stamp = string.Join("|", getPaths().Order(StringComparer.OrdinalIgnoreCase).Select(path =>
			{
				var file = new FileInfo(path);
				return $"{path}:{(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)}:{(file.Exists ? file.Length : 0)}";
			}));
			var changed = stamp != previous;
			previous = stamp;
			return changed;
		}
		catch (IOException)
		{
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}
}
