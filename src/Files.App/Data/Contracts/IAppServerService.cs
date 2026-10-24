// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Server.Core;

namespace Files.App.Data.Contracts;

public interface IAppServerService : IAsyncDisposable
{
	int CloudProviderGroupCount { get; }
	Task InitializeAsync();
	Task SetWindowHandleAsync(long handle);
	Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync();
	Task<ShellNewEntry?> GetShellNewEntryAsync(string extension);
	Task<IReadOnlyList<string>> GetWslDistributionsAsync();
	Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync();
	Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync();
	void InvalidatePinnedFolders();
	Task<IReadOnlyList<ICloudProvider>> GetCloudProvidersAsync(int group);
	void BeginUpdate();
	void EndUpdate();
}
