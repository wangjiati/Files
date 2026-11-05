// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text;

namespace Files.App.UnitTests.Links
{
	[TestClass]
	public sealed class ReparsePointParserTests
	{
		private const uint SymlinkTag = 0xA000000C;
		private const uint MountPointTag = 0xA0000003;

		[TestMethod]
		public void TryParse_AbsoluteSymlink_ReturnsTarget()
		{
			var info = Parse(
				SymlinkTag,
				@"\??\C:\Target\file.txt",
				@"C:\Target\file.txt",
				relative: false,
				linkPath: @"D:\Projects\link.txt");

			Assert.AreEqual(@"C:\Target\file.txt", info.TargetPath);
			Assert.IsFalse(info.IsRelative);
		}

		[TestMethod]
		public void TryParse_RelativeSymlink_ResolvesAgainstParentFolder()
		{
			var info = Parse(
				SymlinkTag,
				@"..\shared\config.yaml",
				@"..\shared\config.yaml",
				relative: true,
				linkPath: @"D:\Projects\2026\config.yaml");

			Assert.AreEqual(@"D:\Projects\shared\config.yaml", info.TargetPath);
			Assert.IsTrue(info.IsRelative);
		}

		[TestMethod]
		public void TryParse_Junction_ReturnsTarget()
		{
			var info = Parse(
				MountPointTag,
				@"\??\D:\Archive\2026",
				@"D:\Archive\2026",
				relative: false,
				linkPath: @"D:\Projects\archive");

			Assert.AreEqual(@"D:\Archive\2026", info.TargetPath);
			Assert.IsFalse(info.IsRelative);
		}

		[TestMethod]
		public void TryParse_VolumeMountPoint_NormalizesVolumeGuidPath()
		{
			var info = Parse(
				MountPointTag,
				@"\??\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\",
				string.Empty,
				relative: false,
				linkPath: @"D:\Data");

			Assert.AreEqual(@"\\?\Volume{8f2a4c1e-7b3d-4a2e-9f10-2c6d8e4a1b77}\", info.TargetPath);
		}

		[TestMethod]
		public void TryParse_ShortBuffer_ReturnsFalse()
		{
			Assert.IsFalse(ReparsePointParser.TryParse(new byte[8], @"C:\link", out _));
		}

		[TestMethod]
		public void TryParse_UnknownTag_ReturnsFalse()
		{
			var buffer = BuildBuffer(0x9000001A, @"C:\Target", string.Empty, relative: false);
			Assert.IsFalse(ReparsePointParser.TryParse(buffer, @"C:\link", out _));
		}

		[TestMethod]
		public void TryParse_EmptyTarget_ReturnsFalse()
		{
			var buffer = BuildBuffer(SymlinkTag, string.Empty, string.Empty, relative: false);
			Assert.IsFalse(ReparsePointParser.TryParse(buffer, @"C:\link", out _));
		}

		[TestMethod]
		public void TryParse_RelativeTargetWithNullCharacter_ReturnsFalseWithoutThrowing()
		{
			var buffer = BuildBuffer(SymlinkTag, "..\\bad\0file", string.Empty, relative: true);

			Assert.IsFalse(ReparsePointParser.TryParse(buffer, @"D:\Projects\link.txt", out _));
		}

		[TestMethod]
		public void TryParse_OutOfRangeOffset_ReturnsFalse()
		{
			var buffer = BuildBuffer(SymlinkTag, @"\??\C:\Target", string.Empty, relative: false);
			buffer[8] = 0xFF;
			buffer[9] = 0x7F;

			Assert.IsFalse(ReparsePointParser.TryParse(buffer, @"C:\link", out _));
		}

		private static ReparsePointInfo Parse(uint tag, string substitute, string printName, bool relative, string linkPath)
		{
			var buffer = BuildBuffer(tag, substitute, printName, relative);
			Assert.IsTrue(ReparsePointParser.TryParse(buffer, linkPath, out var info));
			return info;
		}

		private static byte[] BuildBuffer(uint tag, string substitute, string printName, bool relative)
		{
			var substituteBytes = Encoding.Unicode.GetBytes(substitute);
			var printBytes = Encoding.Unicode.GetBytes(printName);
			var pathStart = tag == SymlinkTag ? 20 : 16;
			var buffer = new byte[pathStart + substituteBytes.Length + printBytes.Length + 2];

			BitConverter.GetBytes(tag).CopyTo(buffer, 0);
			BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
			BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
			BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
			BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(buffer, 10);
			BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(buffer, 12);
			BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, 14);

			if (tag == SymlinkTag)
				BitConverter.GetBytes(relative ? 1u : 0u).CopyTo(buffer, 16);

			substituteBytes.CopyTo(buffer, pathStart);
			printBytes.CopyTo(buffer, pathStart + substituteBytes.Length);

			return buffer;
		}
	}
}
