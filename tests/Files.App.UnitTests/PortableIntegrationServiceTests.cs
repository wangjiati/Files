// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class PortableIntegrationServiceTests
	{
		[TestMethod]
		public async Task JumpListOperations_AreNoOps()
		{
			var service = new PortableJumpListService();

			await service.InitializeAsync();
			await service.AddFolderAsync(@"C:\Data");
			await service.RefreshPinnedFoldersAsync();
			await service.RemoveFolderAsync(@"C:\Data");

			Assert.IsFalse((await service.GetFoldersAsync()).Any());
		}

		[TestMethod]
		public async Task SystemIntegrationOperations_AreNoOps()
		{
			var service = new PortableSystemIntegrationService();

			Assert.AreEqual(StartupTaskState.Disabled, await service.GetStartupTaskStateAsync());
			Assert.IsFalse(service.IsDefaultFileManager());
			Assert.IsFalse(service.IsOpenFileDialog());
			await service.SetStartupTaskEnabledAsync(true);
			Assert.AreEqual(StartupTaskState.Disabled, await service.GetStartupTaskStateAsync());
			await service.SetStartupTaskEnabledAsync(false);
			await service.SetDefaultFileManagerAsync(true);
			Assert.IsFalse(service.IsDefaultFileManager());
			await service.SetDefaultFileManagerAsync(false);
			await service.SetOpenFileDialogAsync(true);
			Assert.IsFalse(service.IsOpenFileDialog());
			await service.SetOpenFileDialogAsync(false);
		}
	}
}
