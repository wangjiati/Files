// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Computes folder levels of item paths relative to a scope root, where "a" is level 1 and "a\b" is level 2.
	/// </summary>
	public static class SubfolderDepth
	{
		/// <summary>
		/// Returns whether <paramref name="relativePath"/> lies within the given number of levels below the scope root ("a" is level 1).
		/// </summary>
		public static bool IsWithinDepth(string relativePath, int depth)
		{
			ArgumentNullException.ThrowIfNull(relativePath);

			int segments = 1;
			foreach (char c in relativePath)
			{
				if (c == '\\')
					segments++;
			}

			return segments <= depth;
		}
	}
}
