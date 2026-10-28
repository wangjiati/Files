// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Helpers
{
	internal static class AppResourceLoader
	{
		public static string ResolvePath(string relativePath)
			=> Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));

		public static Task<string> ReadAllTextAsync(string relativePath)
			=> File.ReadAllTextAsync(ResolvePath(relativePath));
	}
}
