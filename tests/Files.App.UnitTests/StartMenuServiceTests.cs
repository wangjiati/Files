// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class StartMenuServiceTests
	{
		[TestMethod]
		public void IsPinned_ReturnsFalseWithoutPackageIdentity()
		{
			var service = new StartMenuService();

			Assert.IsFalse(service.IsPinned(@"C:\Files.Test"));
		}
	}
}
