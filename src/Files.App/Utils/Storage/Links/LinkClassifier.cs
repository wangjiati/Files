// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Utils.Storage.Links
{
	public static class LinkClassifier
	{
		private const uint SymlinkTag = 0xA000000C;
		private const uint MountPointTag = 0xA0000003;
		private const string NtVolumePrefix = @"\??\Volume{";
		private const string Win32VolumePrefix = @"\\?\Volume{";

		public static LinkKind FromReparseTag(uint reparseTag, bool isDirectory, string? targetPath)
		{
			if (reparseTag == SymlinkTag)
				return LinkKind.SymbolicLink;

			if (reparseTag == MountPointTag)
				return IsVolumeGuidPath(targetPath) ? LinkKind.VolumeMountPoint : LinkKind.Junction;

			return LinkKind.None;
		}

		private static bool IsVolumeGuidPath(string? targetPath)
		{
			return !string.IsNullOrEmpty(targetPath) &&
				(targetPath.StartsWith(NtVolumePrefix, StringComparison.OrdinalIgnoreCase) ||
				 targetPath.StartsWith(Win32VolumePrefix, StringComparison.OrdinalIgnoreCase));
		}
	}
}
