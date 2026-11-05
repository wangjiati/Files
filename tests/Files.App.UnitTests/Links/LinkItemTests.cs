// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Storage;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class LinkItemTests
	{
		[TestMethod]
		public void LinkItem_FileSystemLink_IsNotShortcut()
		{
			var item = new LinkItem(LinkKind.Junction)
			{
				PrimaryItemAttribute = StorageItemTypes.Folder
			};

			Assert.IsTrue(item.IsFileSystemLink);
			Assert.IsFalse(item.IsShortcut);
			Assert.AreEqual(LinkKind.Junction, item.LinkKind);
		}

		[TestMethod]
		public void GitLinkItem_RemainsGitItem()
		{
			var item = new GitLinkItem(LinkKind.SymbolicLink);

			Assert.IsTrue(item is GitItem);
			Assert.IsTrue(item.IsFileSystemLink);
			Assert.IsFalse(item.IsShortcut);
		}
	}
}
