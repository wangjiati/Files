// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	internal static class ClipboardFileOperations
	{
		private static int _fileClipboardSetByThisProcess;

		public static bool WasFileClipboardSetByThisProcess
			=> Volatile.Read(ref _fileClipboardSetByThisProcess) != 0;

		public static void MarkFileClipboardSet()
			=> Volatile.Write(ref _fileClipboardSetByThisProcess, 1);
	}
}
