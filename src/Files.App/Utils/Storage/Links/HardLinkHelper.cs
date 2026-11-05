// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.App.Utils.Storage.Links
{
	public static class HardLinkHelper
	{
		public static uint? GetLinkCount(string path)
		{
			using var handle = Win32Helper.OpenFileForRead(path);
			if (handle.IsInvalid)
				return null;

			FILE_STANDARD_INFO info = default;
			if (!PInvoke.GetFileInformationByHandleEx(
					handle,
					FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo,
					MemoryMarshal.AsBytes(new Span<FILE_STANDARD_INFO>(ref info))))
			{
				return null;
			}

			return info.NumberOfLinks;
		}

		public static bool IsLocalNtfsOrReFs(string path)
		{
			try
			{
				var root = Path.GetPathRoot(path);
				if (string.IsNullOrEmpty(root))
					return false;

				var drive = new DriveInfo(root);
				return drive.DriveType == System.IO.DriveType.Fixed && drive.DriveFormat is "NTFS" or "ReFS";
			}
			catch
			{
				return false;
			}
		}
	}
}
