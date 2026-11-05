// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils.Storage.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class LinkClassifierTests
	{
		private const uint SymlinkTag = 0xA000000C;
		private const uint MountPointTag = 0xA0000003;

		[TestMethod]
		public void FromReparseTag_SymbolicLink_ReturnsSymbolicLink()
		{
			Assert.AreEqual(LinkKind.SymbolicLink, LinkClassifier.FromReparseTag(SymlinkTag, false, null));
		}

		[TestMethod]
		public void FromReparseTag_MountPointToFolder_ReturnsJunction()
		{
			Assert.AreEqual(LinkKind.Junction, LinkClassifier.FromReparseTag(MountPointTag, true, @"D:\Archive\2026"));
		}

		[TestMethod]
		public void FromReparseTag_MountPointToVolumeGuid_ReturnsVolumeMountPoint()
		{
			Assert.AreEqual(
				LinkKind.VolumeMountPoint,
				LinkClassifier.FromReparseTag(MountPointTag, true, @"\\?\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\"));
		}

		[TestMethod]
		public void FromReparseTag_MountPointWithNtPrefix_ReturnsVolumeMountPoint()
		{
			Assert.AreEqual(
				LinkKind.VolumeMountPoint,
				LinkClassifier.FromReparseTag(MountPointTag, true, @"\??\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\"));
		}

		[TestMethod]
		public void FromReparseTag_UnknownTag_ReturnsNone()
		{
			Assert.AreEqual(LinkKind.None, LinkClassifier.FromReparseTag(0x9000001A, false, null));
		}
	}
}
