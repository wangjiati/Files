// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Safe regular expression matcher for directory filters.
	/// Compilation happens once per projection, without <see cref="RegexOptions.Compiled"/> or unbounded backtracking.
	/// </summary>
	public sealed class DirectoryRegexFilter
	{
		public const int MaxPatternLength = 512;

		public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

		private readonly Regex regex;
		private readonly DirectoryRegexTarget target;

		private DirectoryRegexFilter(Regex regex, DirectoryRegexTarget target)
		{
			this.regex = regex;
			this.target = target;
		}

		public static bool TryCreate(DirectoryRegexOptions options, out DirectoryRegexFilter? filter, out DirectoryRegexError error)
		{
			filter = null;

			if (string.IsNullOrEmpty(options.Pattern))
			{
				error = DirectoryRegexError.InvalidPattern;
				return false;
			}

			if (options.Pattern.Length > MaxPatternLength)
			{
				error = DirectoryRegexError.PatternTooLong;
				return false;
			}

			try
			{
				var regexOptions = RegexOptions.CultureInvariant;
				if (options.IgnoreCase)
					regexOptions |= RegexOptions.IgnoreCase;

				filter = new DirectoryRegexFilter(new Regex(options.Pattern, regexOptions, MatchTimeout), options.Target);
				error = DirectoryRegexError.None;
				return true;
			}
			catch (ArgumentException)
			{
				error = DirectoryRegexError.InvalidPattern;
				return false;
			}
		}

		public bool IsMatch(in DirectoryItemFilterSnapshot item)
			=> regex.IsMatch(target switch
			{
				DirectoryRegexTarget.RawName => item.RawName,
				DirectoryRegexTarget.FullPath => item.FullPath,
				_ => item.DisplayName,
			});
	}
}
