// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class AppProcessLauncherTests
	{
		[TestMethod]
		public void NewWindows_AreDisabledOnlyForPortable()
		{
			Assert.IsFalse(AppProcessLauncher.SupportsNewWindows(DistributionKind.Portable));
			Assert.IsTrue(AppProcessLauncher.SupportsNewWindows(DistributionKind.Msix));
		}

		[TestMethod]
		public void ProcessStartInfo_UsesTheProvidedExecutableAndArguments()
		{
			var startInfo = AppProcessLauncher.CreateStartInfo(@"C:\Portable Files\Files.exe", "--new-window");

			Assert.AreEqual(@"C:\Portable Files\Files.exe", startInfo.FileName);
			Assert.AreEqual("--new-window", startInfo.Arguments);
			Assert.IsFalse(startInfo.UseShellExecute);
		}
	}
}
