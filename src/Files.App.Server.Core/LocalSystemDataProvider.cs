// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Server.Core.Cloud;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Windows.Storage;

namespace Files.App.Server.Core;

public sealed class LocalSystemDataProvider
{
	private static readonly TimeSpan lifetime = TimeSpan.FromMinutes(5);

	private readonly CachedTask<IReadOnlyList<ShellNewEntry>> shellNew;
	private readonly CachedTask<IReadOnlyList<string>> wsl;
	private readonly CachedTask<IReadOnlyList<ShellLibraryItem>> libraries;
	private readonly CachedTask<IReadOnlyList<PinnedFolder>> pinnedFolders;
	private readonly CachedTask<IReadOnlyList<ICloudProvider>>[] cloudProviders;
	private readonly ISystemDataSource source;
	private readonly string cacheDirectory;

	public LocalSystemDataProvider(string cacheDirectory, ILogger? logger = null)
		: this(cacheDirectory, logger ?? NullLogger.Instance, new SystemDataSource(logger ?? NullLogger.Instance))
	{
	}

	internal LocalSystemDataProvider(
		string cacheDirectory,
		ILogger logger,
		ISystemDataSource source,
		AbstractCloudDetector[]? detectors = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
		this.cacheDirectory = cacheDirectory;
		this.source = source;
		detectors ??=
		[
			new GoogleDriveCloudDetector(logger), new DropBoxCloudDetector(logger), new BoxCloudDetector(logger),
			new GenericCloudDetector(logger), new SynologyDriveCloudDetector(logger), new OXDriveCloudDetector(logger)
		];

		shellNew = new(() => source.GetShellNewEntriesAsync(cacheDirectory), lifetime, source.HasShellNewChanged);
		wsl = new(source.GetWslDistributionsAsync, lifetime, source.HasWslChanged);
		libraries = new(source.GetLibrariesAsync, lifetime, source.HasLibrariesChanged);
		pinnedFolders = new(source.GetPinnedFoldersAsync, lifetime, source.HasPinnedFoldersChanged);
		cloudProviders = detectors.Select(detector => new CachedTask<IReadOnlyList<ICloudProvider>>(async () =>
			(await detector.DetectCloudProvidersAsync()).Select(NormalizeCloudProvider).ToArray(), lifetime, CreateCloudMonitor(detector))).ToArray();
	}

	public int CloudProviderGroupCount => cloudProviders.Length;

	public Task InitializeAsync() => Task.WhenAll(shellNew.GetAsync(), libraries.GetAsync(), pinnedFolders.GetAsync());

	public Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync() => shellNew.GetAsync();

	public Task<ShellNewEntry?> GetShellNewEntryAsync(string extension) =>
		string.IsNullOrEmpty(extension) ? Task.FromResult<ShellNewEntry?>(null) : Task.Run(() => source.GetShellNewEntryAsync(extension, cacheDirectory));

	public Task<IReadOnlyList<string>> GetWslDistributionsAsync() => wsl.GetAsync();

	public Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync() => libraries.GetAsync();

	public Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync() => pinnedFolders.GetAsync();

	public void InvalidatePinnedFolders() => pinnedFolders.Invalidate();

	public Task<IReadOnlyList<ICloudProvider>> GetCloudProvidersAsync(int group)
	{
		if ((uint)group >= cloudProviders.Length)
			throw new ArgumentOutOfRangeException(nameof(group));
		return cloudProviders[group].GetAsync();
	}

	private static ICloudProvider NormalizeCloudProvider(ICloudProvider provider)
	{
		if (provider.IconData is not { Length: 0 })
			return provider;

		return new CloudProvider(provider.ID)
		{
			Name = provider.Name,
			SyncFolder = provider.SyncFolder,
		};
	}

	private static Func<bool> CreateCloudMonitor(AbstractCloudDetector detector)
	{
		var local = UserDataPaths.GetDefault().LocalAppData;
		var files = new FileStampMonitor(() => detector switch
		{
			GoogleDriveCloudDetector => (string[])[Path.Combine(local, @"Google\DriveFS\root_preference_sqlite.db"), Path.Combine(local, @"Google\DriveFS\root_preference_sqlite.db-wal")],
			DropBoxCloudDetector => Directory.Exists(Path.Combine(local, "Packages"))
				? Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "DropboxInc.Dropbox*")
					.Select(path => Path.Combine(path, @"LocalCache\Local\Dropbox\info.json")).Append(Path.Combine(local, @"Dropbox\info.json"))
				: (string[])[Path.Combine(local, @"Dropbox\info.json")],
			BoxCloudDetector => (string[])[Path.Combine(local, @"Box\Box\data\shell\sync_root_folder.txt"), Path.Combine(local, @"Box Sync\sync_root_folder.txt")],
			SynologyDriveCloudDetector => (string[])[Path.Combine(local, @"SynologyDrive\data\db\sys.sqlite"), Path.Combine(local, @"SynologyDrive\data\db\sys.sqlite-wal")],
			OXDriveCloudDetector => (string[])[Path.Combine(local, @"Open-Xchange\OXDrive\userConfig.json")],
			_ => Array.Empty<string>()
		});
		RegistryChangeMonitor[] registry = detector switch
		{
			GoogleDriveCloudDetector => [new(Registry.CurrentUser, @"Software\Google\DriveFS")],
			OXDriveCloudDetector => [new(Registry.LocalMachine, @"Software\WOW6432Node\Open-Xchange\OXDrive")],
			GenericCloudDetector =>
			[
				new(Registry.CurrentUser, @"Software\Yandex\Yandex.Disk.2"),
				new(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace"),
				new(Registry.CurrentUser, @"Software\Microsoft\OneDrive\Accounts"),
				new(Registry.CurrentUser, @"Software\pCloud"),
				new(Registry.CurrentUser, @"Software\Nutstore"),
				new(Registry.CurrentUser, @"Software\SeaDrive\Seafile Drive Client\Settings"),
				new(Registry.CurrentUser, @"Software\Classes\CLSID"),
				new(Registry.LocalMachine, @"Software\Classes\CLSID"),
				new(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager"),
				new(Registry.LocalMachine, @"Software\Autodesk\Desktop Connector"),
			],
			_ => []
		};
		return () =>
		{
			var changed = files.HasChanged();
			foreach (var monitor in registry)
				changed |= monitor.HasChanged();
			return changed;
		};
	}
}
