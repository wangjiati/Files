// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.RegularExpressions;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class WildcardConverterTests
	{
		// The brief pins assertions on semantics: the caller anchors the fragment with ^...$.
		private static bool IsMatch(string input, string wildcard)
			=> Regex.IsMatch(input, "^" + WildcardConverter.ToRegexPattern(wildcard) + "$", RegexOptions.IgnoreCase);

		[TestMethod]
		public void Star_MatchesRemainderOfName()
		{
			Assert.IsTrue(IsMatch("IMG_2049.jpg", "IMG_*"));
		}

		[TestMethod]
		public void QuestionMark_MatchesExactlyOneCharacter()
		{
			Assert.IsTrue(IsMatch("IMG_2050", "IMG_?050"));
			Assert.IsFalse(IsMatch("IMG_2051", "IMG_?050"));
		}

		[TestMethod]
		public void Dot_IsMatchedLiterally()
		{
			Assert.IsFalse(IsMatch("IMGab", "IMG_."));
			Assert.IsTrue(IsMatch("IMG_.", "IMG_."));
		}
	}
}
