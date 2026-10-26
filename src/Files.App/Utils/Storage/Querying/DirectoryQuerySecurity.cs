// Copyright (c) Files Community
// Licensed under the MIT License.

using SQLitePCL;
using System;
using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Statement shape and SQLite authorizer rules for the read-only query workbench.
	/// The authorizer is a hard boundary: only reads on the private snapshot are allowed.
	/// </summary>
	public static class DirectoryQuerySecurity
	{
		private static readonly HashSet<string> DangerousFunctions = new(StringComparer.OrdinalIgnoreCase)
		{
			"load_extension",
			"readfile",
			"writefile",
			"edit",
			"zipfile",
			"fts3_tokenizer",
			"fts5_tokenizer",
		};

		public static bool IsSelectLike(string sql)
		{
			var text = sql.AsSpan();
			var index = SkipTrivia(text, 0);

			var start = index;
			while (index < text.Length && char.IsLetter(text[index]))
				index++;

			var keyword = text[start..index];

			return keyword.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
				|| keyword.Equals("WITH", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Returns whether the text after the first prepared statement contains anything executable.
		/// Whitespace, trailing semicolons and comments are allowed.
		/// </summary>
		public static bool HasExecutableContent(string? tail)
		{
			if (string.IsNullOrEmpty(tail))
				return false;

			return SkipTrivia(tail.AsSpan(), 0) < tail.Length;
		}

		public static bool IsDangerousFunction(string? functionName)
			=> !string.IsNullOrEmpty(functionName) && DangerousFunctions.Contains(functionName);

		public static int Authorize(
			object userData,
			int actionCode,
			string parameter0,
			string parameter1,
			string databaseName,
			string triggerOrView)
		{
			return actionCode switch
			{
				raw.SQLITE_SELECT or raw.SQLITE_READ or raw.SQLITE_RECURSIVE => raw.SQLITE_OK,
				raw.SQLITE_FUNCTION => IsDangerousFunction(parameter0) ? raw.SQLITE_DENY : raw.SQLITE_OK,
				_ => raw.SQLITE_DENY,
			};
		}

		private static int SkipTrivia(ReadOnlySpan<char> text, int index)
		{
			while (index < text.Length)
			{
				if (char.IsWhiteSpace(text[index]) || text[index] == ';')
				{
					index++;
					continue;
				}

				if (text[index] == '-' && index + 1 < text.Length && text[index + 1] == '-')
				{
					index += 2;
					while (index < text.Length && text[index] is not ('\r' or '\n'))
						index++;
					continue;
				}

				if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
				{
					index += 2;
					while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
						index++;
					index = Math.Min(index + 2, text.Length);
					continue;
				}

				break;
			}

			return index;
		}
	}
}
