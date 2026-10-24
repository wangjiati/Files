// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Server.Core;

internal interface ISystemDataSource
{
	Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync();
	Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync();
	Task<IReadOnlyList<string>> GetWslDistributionsAsync();
	Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync(string cacheDirectory);
	Task<ShellNewEntry?> GetShellNewEntryAsync(string extension, string cacheDirectory);
	bool HasLibrariesChanged();
	bool HasPinnedFoldersChanged();
	bool HasShellNewChanged();
	bool HasWslChanged();
}
