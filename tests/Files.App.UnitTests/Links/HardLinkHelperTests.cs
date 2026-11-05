// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class HardLinkHelperTests
	{
		[TestMethod]
		public void GetLinkCount_NormalFile_ReturnsOne()
		{
			var path = Path.GetTempFileName();
			try
			{
				Assert.AreEqual(1u, HardLinkHelper.GetLinkCount(path)!.Value);
			}
			finally
			{
				File.Delete(path);
			}
		}
	}
}
