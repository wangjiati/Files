// Copyright (c) Files Community
// Licensed under the MIT License.

#if !DISTRIBUTION_MSIX
using Files.App.Server.Core;
using Microsoft.Extensions.Logging;

namespace Files.App.Services;

internal sealed class InProcessAppServerService : IAppServerService
{
	private readonly LocalSystemDataProvider provider;

	internal InProcessAppServerService(string cacheDirectory, ILogger<InProcessAppServerService> logger)
	{
		provider = new LocalSystemDataProvider(cacheDirectory, logger);
		Server.InProcessServer.Changed += (_, name) => App.NotifyServerChanged(name);
	}

	public int CloudProviderGroupCount => provider.CloudProviderGroupCount;
	public async Task InitializeAsync()
	{
		await provider.InitializeAsync();
		await Server.InProcessServer.InitializeAsync();
	}
	public Task SetWindowHandleAsync(long handle) => Task.CompletedTask;
	public Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync() => provider.GetShellNewEntriesAsync();
	public Task<ShellNewEntry?> GetShellNewEntryAsync(string extension) => provider.GetShellNewEntryAsync(extension);
	public Task<IReadOnlyList<string>> GetWslDistributionsAsync() => provider.GetWslDistributionsAsync();
	public Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync() => provider.GetLibrariesAsync();
	public Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync() => provider.GetPinnedFoldersAsync();
	public void InvalidatePinnedFolders()
	{
		provider.InvalidatePinnedFolders();
		Server.PinnedFoldersService.InvalidatePinnedFolders();
	}
	public Task<IReadOnlyList<ICloudProvider>> GetCloudProvidersAsync(int group) => provider.GetCloudProvidersAsync(group);
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
#endif
