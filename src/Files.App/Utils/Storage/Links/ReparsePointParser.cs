// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Files.App.Utils.Storage.Links
{
	public readonly record struct ReparsePointInfo(uint Tag, string TargetPath, bool IsRelative);

	public static class ReparsePointParser
	{
		private const uint SymlinkTag = 0xA000000C;
		private const uint MountPointTag = 0xA0000003;

		public static bool TryParse(ReadOnlySpan<byte> buffer, string linkPath, out ReparsePointInfo info)
		{
			try
			{
				return TryParseCore(buffer, linkPath, out info);
			}
			catch
			{
				info = default;
				return false;
			}
		}

		private static bool TryParseCore(ReadOnlySpan<byte> buffer, string linkPath, out ReparsePointInfo info)
		{
			info = default;

			if (buffer.Length < 16)
				return false;

			var tag = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
			if (tag is not SymlinkTag and not MountPointTag)
				return false;

			var pathStart = tag == SymlinkTag ? 20 : 16;
			if (buffer.Length < pathStart)
				return false;

			var substituteOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));
			var substituteLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(10, 2));
			var printOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(12, 2));
			var printLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(14, 2));

			if (!TryReadString(buffer, pathStart, substituteOffset, substituteLength, out var substitute))
				return false;

			if (!TryReadString(buffer, pathStart, printOffset, printLength, out var printName))
				return false;

			if (string.IsNullOrEmpty(substitute) && string.IsNullOrEmpty(printName))
				return false;

			var isRelative = tag == SymlinkTag &&
				BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4)) == 1;

			string targetPath;
			if (isRelative)
			{
				var parentFolder = Path.GetDirectoryName(linkPath);
				if (string.IsNullOrEmpty(parentFolder))
					return false;

				targetPath = Path.GetFullPath(Path.Combine(parentFolder, substitute));
			}
			else
			{
				targetPath = NormalizeAbsolutePath(string.IsNullOrEmpty(printName) ? substitute : printName);
			}

			if (string.IsNullOrEmpty(targetPath))
				return false;

			info = new ReparsePointInfo(tag, targetPath, isRelative);
			return true;
		}

		private static bool TryReadString(ReadOnlySpan<byte> buffer, int pathStart, ushort offset, ushort length, out string value)
		{
			value = string.Empty;

			if (length == 0)
				return true;

			if (offset % 2 != 0 || length % 2 != 0)
				return false;

			var start = pathStart + offset;
			var end = start + length;
			if (start < pathStart || end > buffer.Length)
				return false;

			value = Encoding.Unicode.GetString(buffer.Slice(start, length));
			return true;
		}

		private static string NormalizeAbsolutePath(string path)
		{
			if (path.StartsWith(@"\??\", StringComparison.Ordinal))
				path = path[4..];

			if (path.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
				return @"\\" + path[4..];

			if (path.StartsWith(@"Volume{", StringComparison.OrdinalIgnoreCase))
				return @"\\?\" + path;

			return path;
		}
	}
}
