// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class AppResourceLoaderTests
	{
		[TestMethod]
		public async Task ReadAllTextAsync_ReadsFileRelativeToAppDirectory()
		{
			var fileName = $"app-resource-{Guid.NewGuid():N}.json";
			var filePath = Path.Combine(AppContext.BaseDirectory, fileName);
			await File.WriteAllTextAsync(filePath, "resource-content");

			try
			{
				Assert.AreEqual("resource-content", await AppResourceLoader.ReadAllTextAsync(fileName));
			}
			finally
			{
				File.Delete(filePath);
			}
		}
	}
}
