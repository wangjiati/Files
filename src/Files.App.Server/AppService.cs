// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Server.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage;

namespace Files.App.Server;

public sealed partial class AppService : IDisposable
{
	private static readonly Lazy<LocalSystemDataProvider> dataProvider = new(() =>
		new LocalSystemDataProvider(ApplicationData.Current.LocalCacheFolder.Path, Program.Logger));

	private readonly long clientId;
	private int disposed;

	public AppService(int processId)
	{
		clientId = ServerHost.AddClient(processId);
	}

	~AppService() => Dispose();

	public static int CloudProviderGroupCount => dataProvider.Value.CloudProviderGroupCount;

	public void SetWindowHandle(long handle) => ServerHost.SetWindow(clientId, handle);

	public static IAsyncAction InitializeAsync() => AsyncInfo.Run(token => dataProvider.Value.InitializeAsync().WaitAsync(token));

	public static IAsyncOperation<IReadOnlyList<ShellNewEntryInfo>> GetShellNewEntriesAsync() => AsyncInfo.Run<IReadOnlyList<ShellNewEntryInfo>>(async token =>
		(await dataProvider.Value.GetShellNewEntriesAsync().WaitAsync(token)).Select(entry => new ShellNewEntryInfo(entry)).ToList().AsReadOnly());

	public static IAsyncOperation<ShellNewEntryInfo?> GetShellNewEntryAsync(string extension) => AsyncInfo.Run(async token =>
	{
		var entry = await dataProvider.Value.GetShellNewEntryAsync(extension).WaitAsync(token);
		return entry is null ? null : new ShellNewEntryInfo(entry);
	});

	public static IAsyncOperation<IReadOnlyList<string>> GetWslDistributionsAsync() =>
		AsyncInfo.Run(token => dataProvider.Value.GetWslDistributionsAsync().WaitAsync(token));

	public static IAsyncOperation<IReadOnlyList<LibraryInfo>> GetLibrariesAsync() => AsyncInfo.Run<IReadOnlyList<LibraryInfo>>(async token =>
		(await dataProvider.Value.GetLibrariesAsync().WaitAsync(token)).Select(library => new LibraryInfo(library)).ToList().AsReadOnly());

	public static IAsyncOperation<IReadOnlyList<PinnedFolderInfo>> GetPinnedFoldersAsync() => AsyncInfo.Run<IReadOnlyList<PinnedFolderInfo>>(async token =>
		(await dataProvider.Value.GetPinnedFoldersAsync().WaitAsync(token))
			.Select(folder => new PinnedFolderInfo(folder.Path, folder.Name, folder.Pidl, folder.IsPinned)).ToList().AsReadOnly());

	public static void InvalidatePinnedFolders() => dataProvider.Value.InvalidatePinnedFolders();

	public static IAsyncOperation<IReadOnlyList<CloudProviderInfo>> GetCloudProvidersAsync(int group) => AsyncInfo.Run<IReadOnlyList<CloudProviderInfo>>(async token =>
		(await dataProvider.Value.GetCloudProvidersAsync(group).WaitAsync(token)).Select(provider => new CloudProviderInfo(provider)).ToList().AsReadOnly());

	public static void BeginUpdate() => ServerHost.BeginUpdate();

	public static void EndUpdate() => ServerHost.EndUpdate();

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 0)
			ServerHost.RemoveClient(clientId);
		GC.SuppressFinalize(this);
	}
}
