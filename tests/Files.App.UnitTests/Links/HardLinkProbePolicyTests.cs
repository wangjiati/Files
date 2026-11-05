// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class HardLinkProbePolicyTests
	{
		[TestMethod]
		public void ShouldProbe_NormalLocalFile_ReturnsTrue()
		{
			Assert.IsTrue(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: false, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_Folder_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: true, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_ReparsePoint_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.ReparsePoint, isFolder: false, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_OfflineFile_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Offline, isFolder: false, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_RecallOnOpen_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe((FileAttributes)0x00040000, isFolder: false, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_RecallOnDataAccess_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe((FileAttributes)0x00400000, isFolder: false, isLocalNtfsOrReFs: true));
		}

		[TestMethod]
		public void ShouldProbe_NonLocalVolume_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(FileAttributes.Normal, isFolder: false, isLocalNtfsOrReFs: false));
		}

		[TestMethod]
		public void ShouldProbe_UnknownAttributes_ReturnsFalse()
		{
			Assert.IsFalse(HardLinkProbePolicy.ShouldProbe(null, isFolder: false, isLocalNtfsOrReFs: true));
		}
	}
}
