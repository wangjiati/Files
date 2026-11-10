// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Server;

/// <summary>
/// Runs the server catalogs inside the portable process without COM activation.
/// </summary>
public static class InProcessServer
{
	// The portable client is the only listener; a fixed id keeps event batching per process.
	private const long ClientId = -1;

	public static event EventHandler<string>? Changed
	{
		add
		{
			if (value is not null)
				ServerEvents.Add(ClientId, value);
		}
		remove
		{
			if (value is not null)
				ServerEvents.Remove(ClientId, value);
		}
	}

	public static Task InitializeAsync() => InitializeCoreAsync();

	public static void BeginUpdate() => ServerEvents.BeginPinnedFoldersUpdate(ClientId);

	public static void EndUpdate() => ServerEvents.EndPinnedFoldersUpdate(ClientId);

	public static RecycleBinChange[] GetRecycleBinChanges() => RecycleBinCatalog.GetChanges(ClientId);

	public static void Stop()
	{
		ServerEvents.RemoveClient(ClientId);
		CatalogWatchers.Dispose();
		DriveCatalog.Stop();
	}

	private static async Task InitializeCoreAsync()
	{
		CatalogWatchers.Initialize();

		await Task.WhenAll(
			SafetyExtensions.IgnoreExceptions(InitializationCache.InitializeAsync, Program.Logger),
			SafetyExtensions.IgnoreExceptions(LocationCatalog.RecentFiles.GetAsync, Program.Logger),
			SafetyExtensions.IgnoreExceptions(LocationCatalog.RecentFolders.GetAsync, Program.Logger),
			SafetyExtensions.IgnoreExceptions(LocationCatalog.NetworkShortcuts.GetAsync, Program.Logger),
			WarmDrivesAsync());
	}

	private static async Task WarmDrivesAsync()
	{
		var ids = await SafetyExtensions.IgnoreExceptions(DriveCatalog.GetIdsAsync, Program.Logger);
		foreach (var id in ids ?? [])
			_ = SafetyExtensions.IgnoreExceptions(() => DriveCatalog.GetAsync(id), Program.Logger);
	}
}
