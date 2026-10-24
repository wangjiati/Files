// Copyright (c) Files Community
// Licensed under the MIT License.

#if DISTRIBUTION_MSIX
using Files.App.Server.Core;

namespace Files.App.Services;

internal sealed class PackagedAppServerService : IAppServerService
{
	private readonly Task<Server.AppService> serverSession;

	internal PackagedAppServerService()
	{
		serverSession = Task.Run(() => new Server.AppService(Environment.ProcessId));
	}

	public int CloudProviderGroupCount => Server.AppService.CloudProviderGroupCount;

	public async Task InitializeAsync()
	{
		await serverSession;
		await Server.AppService.InitializeAsync();
	}

	public async Task SetWindowHandleAsync(long handle) => (await serverSession).SetWindowHandle(handle);

	public async Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync() =>
		(await Server.AppService.GetShellNewEntriesAsync()).Select(ToEntry).ToArray();

	public async Task<ShellNewEntry?> GetShellNewEntryAsync(string extension)
	{
		var entry = await Server.AppService.GetShellNewEntryAsync(extension);
		return entry is null ? null : ToEntry(entry);
	}

	public async Task<IReadOnlyList<string>> GetWslDistributionsAsync() =>
		(await Server.AppService.GetWslDistributionsAsync()).ToArray();

	public async Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync() =>
		(await Server.AppService.GetLibrariesAsync()).Select(library => new ShellLibraryItem
		{
			FullPath = library.FullPath,
			AbsolutePath = library.AbsolutePath,
			RelativePath = library.RelativePath,
			DisplayName = library.DisplayName,
			IsPinned = library.IsPinned,
			DefaultSaveFolder = library.DefaultSaveFolder,
			Folders = library.Folders,
		}).ToArray();

	public async Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync() =>
		(await Server.AppService.GetPinnedFoldersAsync())
			.Select(folder => new PinnedFolder(folder.Path, folder.Name, folder.Pidl, folder.IsPinned)).ToArray();

	public void InvalidatePinnedFolders() => Server.AppService.InvalidatePinnedFolders();

	public async Task<IReadOnlyList<ICloudProvider>> GetCloudProvidersAsync(int group) =>
		(await Server.AppService.GetCloudProvidersAsync(group)).Select(provider => (ICloudProvider)new CloudProvider((CloudProviders)provider.Id)
		{
			Name = provider.Name,
			SyncFolder = provider.SyncFolder,
			IconData = provider.IconData.Length == 0 ? null : provider.IconData,
		}).ToArray();

	public void BeginUpdate() => Server.AppService.BeginUpdate();
	public void EndUpdate() => Server.AppService.EndUpdate();

	public async ValueTask DisposeAsync() => (await serverSession).Dispose();

	private static ShellNewEntry ToEntry(Server.ShellNewEntryInfo entry) => new()
	{
		Extension = entry.Extension,
		Name = entry.Name,
		Command = string.IsNullOrEmpty(entry.Command) ? null : entry.Command,
		Template = string.IsNullOrEmpty(entry.Template) ? null : entry.Template,
		IconData = entry.IconData,
		Data = entry.HasData ? entry.Data : null,
	};
}
#endif
