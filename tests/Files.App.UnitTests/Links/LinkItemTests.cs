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

		[TestMethod]
		public void LinkItem_ExecutableTarget_IsExecutable()
		{
			var item = new LinkItem(LinkKind.SymbolicLink)
			{
				LinkTargetPath = @"D:\Tools\tool.exe"
			};

			Assert.IsTrue(item.IsExecutable);
		}

		[TestMethod]
		public void GitLinkItem_ScriptTarget_IsScriptFile()
		{
			var item = new GitLinkItem(LinkKind.SymbolicLink)
			{
				LinkTargetPath = @"D:\Tools\deploy.ps1"
			};

			Assert.IsTrue(item.IsScriptFile);
		}
	}
}
