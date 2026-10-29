// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Server.Core;
using Files.App.Server.Core.Cloud;
using Files.Shared;
using Files.Storage;
using Files.Shared.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class LocalSystemDataProviderTests
	{
		[TestMethod]
		public async Task Libraries_ReturnTheSharedSystemSnapshot()
		{
			var source = new FakeSystemDataSource
			{
				Libraries = new List<ShellLibraryItem>
				{
					new ShellLibraryItem
					{
						FullPath = @"C:\Users\Test\Documents.library-ms",
						DisplayName = "Documents",
						DefaultSaveFolder = @"C:\Users\Test\Documents",
						Folders = [@"C:\Users\Test\Documents"],
						IsPinned = true,
					},
				},
			};
			var provider = CreateProvider(source);

			var libraries = await provider.GetLibrariesAsync();

			var library = libraries.Single();
			Assert.AreEqual("Documents", library.DisplayName);
			Assert.AreEqual(@"C:\Users\Test\Documents", library.DefaultSaveFolder);
			Assert.IsTrue(library.IsPinned);
		}

		[TestMethod]
		public async Task QuickAccess_ReturnsPathsNamesPidlsAndPinnedState()
		{
			var source = new FakeSystemDataSource
			{
				PinnedFolders = new List<PinnedFolder> { new(@"C:\Work", "Work", [1, 2, 3], true) },
			};
			var provider = CreateProvider(source);

			var folders = await provider.GetPinnedFoldersAsync();

			var folder = folders.Single();
			Assert.AreEqual(@"C:\Work", folder.Path);
			Assert.AreEqual("Work", folder.Name);
			CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, folder.Pidl);
			Assert.IsTrue(folder.IsPinned);
		}

		[TestMethod]
		public async Task Wsl_ReturnsDistributionNames()
		{
			var source = new FakeSystemDataSource { WslDistributions = new List<string> { "Ubuntu", "Debian" } };
			var provider = CreateProvider(source);

			var distributions = await provider.GetWslDistributionsAsync();

			CollectionAssert.AreEqual(new[] { "Ubuntu", "Debian" }, distributions.ToArray());
		}

		[TestMethod]
		public async Task ShellNew_UsesTheInjectedCacheDirectoryAndReturnsEntries()
		{
			var cacheDirectory = Path.Combine(Path.GetTempPath(), "Files-provider-cache", Guid.NewGuid().ToString("N"));
			var entry = new ShellNewEntry { Extension = ".txt", Name = "Text Document", Data = [1, 2] };
			var source = new FakeSystemDataSource
			{
				ShellNewEntries = new List<ShellNewEntry> { entry },
				ShellNewEntry = entry,
			};
			var provider = CreateProvider(source, cacheDirectory);

			var entries = await provider.GetShellNewEntriesAsync();
			var selected = await provider.GetShellNewEntryAsync(".txt");

			Assert.AreEqual(cacheDirectory, source.ShellNewCacheDirectory);
			Assert.AreSame(entry, entries.Single());
			Assert.AreSame(entry, selected);
		}

		[TestMethod]
		public async Task Cloud_ReturnsEachDetectorGroupWithoutWaitingForOtherGroups()
		{
			var fast = new ControlledCloudDetector(new CloudProvider(CloudProviders.DropBox) { Name = "Fast", IconData = [] });
			var slow = new ControlledCloudDetector(new CloudProvider(CloudProviders.Box) { Name = "Slow" });
			var provider = CreateProvider(new FakeSystemDataSource(), detectors: [fast, slow]);

			fast.Release();
			var slowResult = provider.GetCloudProvidersAsync(1);
			var fastResult = await provider.GetCloudProvidersAsync(0).WaitAsync(TimeSpan.FromSeconds(1));

			Assert.AreEqual(2, provider.CloudProviderGroupCount);
			var fastProvider = fastResult.Single();
			Assert.AreEqual("Fast", fastProvider.Name);
			Assert.IsNull(fastProvider.IconData);
			Assert.IsFalse(slowResult.IsCompleted);
			slow.Release();
			Assert.AreEqual("Slow", (await slowResult.WaitAsync(TimeSpan.FromSeconds(1))).Single().Name);
		}

		private static LocalSystemDataProvider CreateProvider(
			ISystemDataSource source,
			string? cacheDirectory = null,
			AbstractCloudDetector[]? detectors = null) =>
			new(cacheDirectory ?? Path.Combine(Path.GetTempPath(), "Files-tests"), NullLogger.Instance, source, detectors ?? []);

		private sealed class ControlledCloudDetector(ICloudProvider provider) : AbstractCloudDetector(NullLogger.Instance)
		{
			private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

			public void Release() => gate.SetResult();

			protected override async IAsyncEnumerable<ICloudProvider> GetProviders()
			{
				await gate.Task;
				yield return provider;
			}
		}

		private sealed class FakeSystemDataSource : ISystemDataSource
		{
			public IReadOnlyList<ShellLibraryItem> Libraries { get; init; } = [];
			public IReadOnlyList<PinnedFolder> PinnedFolders { get; init; } = [];
			public IReadOnlyList<string> WslDistributions { get; init; } = [];
			public IReadOnlyList<ShellNewEntry> ShellNewEntries { get; init; } = [];
			public ShellNewEntry? ShellNewEntry { get; init; }
			public string? ShellNewCacheDirectory { get; private set; }

			public Task<IReadOnlyList<ShellLibraryItem>> GetLibrariesAsync() => Task.FromResult(Libraries);
			public Task<IReadOnlyList<PinnedFolder>> GetPinnedFoldersAsync() => Task.FromResult(PinnedFolders);
			public Task<IReadOnlyList<string>> GetWslDistributionsAsync() => Task.FromResult(WslDistributions);
			public Task<ShellNewEntry?> GetShellNewEntryAsync(string extension, string cacheDirectory)
			{
				ShellNewCacheDirectory = cacheDirectory;
				return Task.FromResult(ShellNewEntry);
			}
			public Task<IReadOnlyList<ShellNewEntry>> GetShellNewEntriesAsync(string cacheDirectory)
			{
				ShellNewCacheDirectory = cacheDirectory;
				return Task.FromResult(ShellNewEntries);
			}
			public bool HasLibrariesChanged() => false;
			public bool HasPinnedFoldersChanged() => false;
			public bool HasShellNewChanged() => false;
			public bool HasWslChanged() => false;
		}
	}
}
