// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services
{
	internal sealed class PortableJumpListService : IWindowsJumpListService
	{
		public Task InitializeAsync() => Task.CompletedTask;
		public Task AddFolderAsync(string path) => Task.CompletedTask;
		public Task RefreshPinnedFoldersAsync() => Task.CompletedTask;
		public Task RemoveFolderAsync(string path) => Task.CompletedTask;
		public Task<IEnumerable<string>> GetFoldersAsync() => Task.FromResult<IEnumerable<string>>([]);
	}
}
