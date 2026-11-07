// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.Update;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Update
{
	[TestClass]
	public sealed class GitHubReleaseParserTests
	{
		private const string ValidJson = """
		{
		  "tag_name": "v4.3.1.7",
		  "html_url": "https://github.com/wangjiati/Files/releases/tag/v4.3.1.7",
		  "body": "## 本版变化\n- 测试"
		}
		""";

		[TestMethod]
		public void TryParseLatestRelease_ReadsVersionNotesAndUrl()
		{
			Assert.IsTrue(GitHubReleaseParser.TryParseLatestRelease(ValidJson, out var release));
			Assert.AreEqual(new Version(4, 3, 1, 7), release.Version);
			Assert.AreEqual("https://github.com/wangjiati/Files/releases/tag/v4.3.1.7", release.PageUrl.ToString());
			Assert.IsTrue(release.Notes!.Contains("本版变化"));
		}

		[TestMethod]
		public void TryParseLatestRelease_AcceptsHistoricalTag()
		{
			var json = ValidJson.Replace("v4.3.1.7", "v4.3.1.0-portable");
			Assert.IsTrue(GitHubReleaseParser.TryParseLatestRelease(json, out var release));
			Assert.AreEqual(new Version(4, 3, 1, 0), release.Version);
		}

		[TestMethod]
		public void TryParseLatestRelease_RejectsMissingUrl()
		{
			var json = """{ "tag_name": "v4.3.1.7", "body": "" }""";
			Assert.IsFalse(GitHubReleaseParser.TryParseLatestRelease(json, out _));
		}

		[TestMethod]
		public void TryParseLatestRelease_RejectsInvalidJson()
		{
			Assert.IsFalse(GitHubReleaseParser.TryParseLatestRelease("{ not json", out _));
		}

		[TestMethod]
		public void ToPreview_TruncatesLongNotes()
		{
			Assert.AreEqual(string.Empty, UpdateNotesFormatter.ToPreview(null));
			Assert.AreEqual("abc", UpdateNotesFormatter.ToPreview("abc", 10));
			var preview = UpdateNotesFormatter.ToPreview(new string('a', 2000), 1500);
			Assert.AreEqual(1501, preview.Length);
			Assert.IsTrue(preview.EndsWith('…'));
		}
	}
}
