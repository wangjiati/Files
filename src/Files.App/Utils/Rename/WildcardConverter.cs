// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Converts a DOS-style wildcard expression into a .NET regular expression fragment for batch rename.
	/// </summary>
	/// <remarks>
	/// <see cref="FileSystemName.TranslateWin32Expression"/> returns a Win32 expression rather than a regex:
	/// '?' becomes the DOS question mark '&gt;', a trailing "*." becomes the DOS star '&lt;', and all other
	/// characters (including regex metacharacters) pass through unescaped. This converter maps that alphabet
	/// onto a regex fragment without '^'/'$' anchors; the caller adds them. Every star becomes a capture
	/// group so a wildcard replacement string can map '*' back to the matched text. The DOS star is
	/// deliberately treated like a plain star: its "stop at the final dot" distinction only shows when the
	/// matched span contains an interior dot (a trailing extension), which has no meaning when renaming.
	/// The DOS dot '"' is never emitted by
	/// <see cref="FileSystemName.TranslateWin32Expression"/> and is therefore not handled.
	/// </remarks>
	public static class WildcardConverter
	{
		/// <summary>
		/// Returns the regex fragment for the given wildcard expression, without anchors.
		/// </summary>
		public static string ToRegexPattern(string wildcard)
		{
			ArgumentNullException.ThrowIfNull(wildcard);

			// Normalize DOS quirks first: empty becomes "*", a trailing "*." becomes the DOS star, '?' becomes the DOS question mark.
			string win32Expression = FileSystemName.TranslateWin32Expression(wildcard);

			StringBuilder builder = new();
			foreach (char c in win32Expression)
			{
				switch (c)
				{
					case '*':
					case '<':
						builder.Append("(.*)");
						break;
					case '>':
						builder.Append("[^.]");
						break;
					default:
						builder.Append(Regex.Escape(c.ToString()));
						break;
				}
			}

			return builder.ToString();
		}
	}
}
