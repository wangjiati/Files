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

		[TestMethod]
		public async Task PortableRelaunchFailure_ReacquiresLeaseAndProtectsData()
		{
			var dataDirectory = CreateTemporaryDirectory();
			PortableDataLease? lease = PortableDataLease.Acquire(dataDirectory);
			try
			{
				var started = await AppProcessLauncher.RelaunchAsync(
					@"C:\Missing\Files.exe",
					(_, _) => false,
					() => { lease?.Dispose(); lease = null; },
					() => lease = PortableDataLease.Acquire(dataDirectory));

				Assert.IsFalse(started);
				Assert.IsNotNull(lease);
				Assert.ThrowsExactly<IOException>(() => PortableDataLease.Acquire(dataDirectory).Dispose());
			}
			finally
			{
				lease?.Dispose();
				Directory.Delete(dataDirectory, true);
			}
		}

		[TestMethod]
		public async Task PortableRelaunchSuccess_ReleasesLeaseForNewProcess()
		{
			var dataDirectory = CreateTemporaryDirectory();
			PortableDataLease? lease = PortableDataLease.Acquire(dataDirectory);
			PortableDataLease? newProcessLease = null;
			var restoreCalls = 0;
			try
			{
				var started = await AppProcessLauncher.RelaunchAsync(
					@"C:\Portable Files\Files.exe",
					(_, _) => true,
					() => { lease?.Dispose(); lease = null; },
					() => restoreCalls++);
				newProcessLease = PortableDataLease.Acquire(dataDirectory);

				Assert.IsTrue(started);
				Assert.IsNull(lease);
				Assert.AreEqual(0, restoreCalls);
			}
			finally
			{
				newProcessLease?.Dispose();
				lease?.Dispose();
				Directory.Delete(dataDirectory, true);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			var path = Path.Combine(Path.GetTempPath(), $"Files-relaunch-{Guid.NewGuid():N}");
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
