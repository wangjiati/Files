// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Storage.Links
{
	public static class HardLinkProbePolicy
	{
		private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
		private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

		public static bool ShouldProbe(FileAttributes? attributes, bool isFolder, bool isLocalNtfsOrReFs)
		{
			if (attributes is null || isFolder || !isLocalNtfsOrReFs)
				return false;

			var excluded = FileAttributes.ReparsePoint | FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;
			return (attributes.Value & excluded) == 0;
		}
	}
}
