// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Text;

namespace Files.App
{
	internal static class AtomicFile
	{
		public static void WriteAllText(string filePath, string text)
		{
			var directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("The data file must have a parent directory.");
			Directory.CreateDirectory(directory);
			var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

			try
			{
				using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
						writer.Write(text);
					stream.Flush(true);
				}

				File.Move(temporaryPath, filePath, true);
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}
	}
}
