// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class LinkTypeHelperTests
	{
		[TestMethod]
		public void Compose_BothParts_JoinsWithSeparator()
		{
			Assert.AreEqual("文本文档 · 符号链接", LinkTypeHelper.Compose("文本文档", "符号链接"));
		}

		[TestMethod]
		public void Compose_FolderAndJunction_JoinsWithSeparator()
		{
			Assert.AreEqual("文件夹 · 目录联接", LinkTypeHelper.Compose("文件夹", "目录联接"));
		}

		[TestMethod]
		public void Compose_MissingBaseType_ReturnsLinkLabel()
		{
			Assert.AreEqual("符号链接", LinkTypeHelper.Compose(null, "符号链接"));
		}

		[TestMethod]
		public void Compose_MissingLinkLabel_ReturnsBaseType()
		{
			Assert.AreEqual("文本文档", LinkTypeHelper.Compose("文本文档", null));
		}
	}
}
