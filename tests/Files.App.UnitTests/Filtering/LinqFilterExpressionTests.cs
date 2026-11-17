// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class LinqFilterExpressionTests
	{
		private static DirectoryItemFilterSnapshot Snapshot(
			string name = "report.txt",
			long? size = null,
			DateTimeOffset? modified = null,
			string? extension = ".txt",
			bool isFolder = false)
			=> new(
				1,
				name,
				name,
				$@"C:\Test\{name}",
				extension,
				isFolder ? "文件夹" : "文本文档",
				new List<string>(),
				size,
				modified,
				modified,
				isFolder);

		[TestMethod]
		public void NameContains_IsCaseInsensitive()
		{
			Assert.IsTrue(LinqFilterExpression.TryParse("Name.Contains(\"REPORT\")", out var predicate, out _));

			Assert.IsTrue(predicate!(Snapshot("Q3 Report.txt")));
			Assert.IsFalse(predicate!(Snapshot("notes.md")));
		}

		[TestMethod]
		public void AndCombinesTextAndSize()
		{
			Assert.IsTrue(LinqFilterExpression.TryParse("Name.Contains(\"report\") && Size > 1MB", out var predicate, out _));

			Assert.IsTrue(predicate!(Snapshot("report.txt", 2L * 1024 * 1024)));
			Assert.IsFalse(predicate!(Snapshot("report.txt", 512L * 1024)));
			Assert.IsFalse(predicate!(Snapshot("notes.md", 2L * 1024 * 1024)));
		}

		[TestMethod]
		public void OrIsEvaluatedAfterAnd()
		{
			Assert.IsTrue(LinqFilterExpression.TryParse("Name.Contains(\"a\") || Name.Contains(\"b\") && Size > 1MB", out var predicate, out _));

			Assert.IsTrue(predicate!(Snapshot("alpha.txt", 0)));
			Assert.IsFalse(predicate!(Snapshot("beta.txt", 0)));
			Assert.IsTrue(predicate!(Snapshot("beta.txt", 2L * 1024 * 1024)));
		}

		[TestMethod]
		public void ModifiedDateComparesAgainstDateLiteral()
		{
			Assert.IsTrue(LinqFilterExpression.TryParse("ModifiedDate > \"2026-01-01\"", out var predicate, out _));

			Assert.IsTrue(predicate!(Snapshot(modified: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))));
			Assert.IsFalse(predicate!(Snapshot(modified: new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero))));
		}

		[TestMethod]
		public void IsFolderBindsBooleanLiteral()
		{
			Assert.IsTrue(LinqFilterExpression.TryParse("IsFolder == true", out var predicate, out _));

			Assert.IsTrue(predicate!(Snapshot("sub", isFolder: true)));
			Assert.IsFalse(predicate!(Snapshot("file.txt")));
		}

		[TestMethod]
		public void PlainTextIsNotTreatedAsLinq()
		{
			Assert.IsFalse(LinqFilterExpression.TryParse("report", out _, out _));
			Assert.IsFalse(LinqFilterExpression.TryParse("Name.Contains(", out _, out _));
		}
	}
}
