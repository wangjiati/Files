// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	public enum FileListExportFormat
	{
		Csv,
		Excel,
	}

	/// <summary>
	/// Counters for one export run, updated during enumeration.
	/// </summary>
	public sealed class FileListExportContext
	{
		public long SkippedCount { get; private set; }

		public void AddSkipped(int count = 1)
		{
			if (count > 0)
				SkippedCount += count;
		}
	}
}
