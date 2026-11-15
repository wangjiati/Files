// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryFilterEvaluatorTests
	{
		private static readonly DateTimeOffset BaseDate = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

		[TestMethod]
		public void EmptySpec_ReturnsEveryItemInOrder()
		{
			var items = new[]
			{
				CreateItem(1, "Alpha.txt"),
				CreateItem(2, "Beta.log"),
				CreateItem(3, "Gamma"),
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, DirectoryFilterSpec.Empty);

			CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, result.ToArray());
		}

		[TestMethod]
		[DataRow(DirectoryTextOperator.Contains, "report", true)]
		[DataRow(DirectoryTextOperator.Contains, "REPORT", true)]
		[DataRow(DirectoryTextOperator.Contains, "annual", false)]
		[DataRow(DirectoryTextOperator.NotContains, "annual", true)]
		[DataRow(DirectoryTextOperator.NotContains, "report", false)]
		[DataRow(DirectoryTextOperator.StartsWith, "rep", true)]
		[DataRow(DirectoryTextOperator.StartsWith, "port", false)]
		[DataRow(DirectoryTextOperator.EndsWith, ".txt", true)]
		[DataRow(DirectoryTextOperator.EndsWith, "report", false)]
		[DataRow(DirectoryTextOperator.Equals, "report.txt", true)]
		[DataRow(DirectoryTextOperator.Equals, "Report.TXT", true)]
		[DataRow(DirectoryTextOperator.Equals, "report", false)]
		public void NameOperator_MatchesAgainstDisplayName(DirectoryTextOperator textOperator, string text, bool expected)
		{
			var items = new[] { CreateItem(1, "report.txt") };
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = textOperator,
					Text = text,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(expected ? 1 : 0, result.Count);
		}

		[TestMethod]
		public void NameOperator_UsesDisplayNameWithoutHiddenExtension()
		{
			var items = new[] { CreateItem(1, "report.txt", displayName: "report") };
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = DirectoryTextOperator.EndsWith,
					Text = ".txt",
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(0, result.Count);
		}

		[TestMethod]
		public void NameContains_AppliesToDisplayName()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt"),
				CreateItem(2, "notes.txt"),
			};
			var spec = new DirectoryFilterSpec { NameContains = "rep" };

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 1 }, result.ToArray());
		}

		[TestMethod]
		public void TypeValues_UseOrSemantics()
		{
			var items = new[]
			{
				CreateItem(1, "a.txt", itemType: "Text Document"),
				CreateItem(2, "b.png", itemType: "PNG Image"),
				CreateItem(3, "c.pdf", itemType: "PDF Document"),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Type,
					Values = ["Text Document", "PNG Image"],
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 1, 2 }, result.ToArray());
		}

		[TestMethod]
		public void ExtensionValues_IgnoreLeadingDotAndCase()
		{
			var items = new[]
			{
				CreateItem(1, "a.txt", extension: ".txt"),
				CreateItem(2, "b.TXT", extension: ".TXT"),
				CreateItem(3, "c.png", extension: ".png"),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Extension,
					Values = ["txt"],
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 1, 2 }, result.ToArray());
		}

		[TestMethod]
		public void ExtensionIncludeEmpty_MatchesItemsWithoutExtension()
		{
			var items = new[]
			{
				CreateItem(1, "folder", extension: null, isFolder: true),
				CreateItem(2, "a.txt", extension: ".txt"),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Extension,
					IncludeEmpty = true,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 1 }, result.ToArray());
		}

		[TestMethod]
		public void TagValues_UseOrSemanticsAndIncludeEmpty()
		{
			var items = new[]
			{
				CreateItem(1, "a.txt", tags: ["work"]),
				CreateItem(2, "b.txt", tags: ["personal"]),
				CreateItem(3, "c.txt", tags: []),
			};
			var matchingSpec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Tags,
					Values = ["work", "personal"],
				}],
			};
			var emptySpec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Tags,
					IncludeEmpty = true,
				}],
			};

			CollectionAssert.AreEqual(new long[] { 1, 2 }, DirectoryFilterEvaluator.Evaluate(items, matchingSpec).ToArray());
			CollectionAssert.AreEqual(new long[] { 3 }, DirectoryFilterEvaluator.Evaluate(items, emptySpec).ToArray());
		}

		[TestMethod]
		public void SizeRange_IsInclusive()
		{
			var items = new[]
			{
				CreateItem(1, "small.txt", size: 1024),
				CreateItem(2, "edge.txt", size: 4096),
				CreateItem(3, "large.txt", size: 8192),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Size,
					MinSize = 4096,
					MaxSize = 8192,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 2, 3 }, result.ToArray());
		}

		[TestMethod]
		public void SizeCondition_ExcludesUnknownSizeInsteadOfTreatingItAsZero()
		{
			var items = new[]
			{
				CreateItem(1, "folder", size: null, isFolder: true),
				CreateItem(2, "known.txt", size: 0),
			};
			var atMostZero = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Size,
					MaxSize = 0,
				}],
			};
			var anySize = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Size,
					MinSize = 0,
				}],
			};

			CollectionAssert.AreEqual(new long[] { 2 }, DirectoryFilterEvaluator.Evaluate(items, atMostZero).ToArray());
			CollectionAssert.AreEqual(new long[] { 2 }, DirectoryFilterEvaluator.Evaluate(items, anySize).ToArray());
		}

		[TestMethod]
		public void DateRanges_AreInclusiveAndExcludeUnknownDates()
		{
			var items = new[]
			{
				CreateItem(1, "before.txt", modified: BaseDate.AddDays(-1), created: BaseDate.AddDays(10)),
				CreateItem(2, "start.txt", modified: BaseDate, created: BaseDate.AddDays(10)),
				CreateItem(3, "inside.txt", modified: BaseDate.AddDays(2), created: BaseDate.AddDays(10)),
				CreateItem(4, "after.txt", modified: BaseDate.AddDays(5), created: BaseDate.AddDays(10)),
				CreateItem(5, "unknown.txt", modified: null, created: null),
			};
			var modifiedSpec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.ModifiedDate,
					MinDate = BaseDate,
					MaxDate = BaseDate.AddDays(2),
				}],
			};
			var createdSpec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.CreatedDate,
					MinDate = BaseDate.AddDays(10),
					MaxDate = BaseDate.AddDays(10),
				}],
			};

			CollectionAssert.AreEqual(new long[] { 2, 3 }, DirectoryFilterEvaluator.Evaluate(items, modifiedSpec).ToArray());
			CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4 }, DirectoryFilterEvaluator.Evaluate(items, createdSpec).ToArray());
		}

		[TestMethod]
		public void DifferentColumns_UseAndSemantics()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt", itemType: "Text Document", extension: ".txt", size: 2048, modified: BaseDate),
				CreateItem(2, "report.png", itemType: "PNG Image", extension: ".png", size: 2048, modified: BaseDate),
				CreateItem(3, "notes.txt", itemType: "Text Document", extension: ".txt", size: 2048, modified: BaseDate),
				CreateItem(4, "report-old.txt", itemType: "Text Document", extension: ".txt", size: 2048, modified: BaseDate.AddDays(-30)),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Name,
						Operator = DirectoryTextOperator.StartsWith,
						Text = "report",
					},
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Type,
						Values = ["Text Document"],
					},
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Size,
						MinSize = 1024,
					},
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.ModifiedDate,
						MinDate = BaseDate.AddDays(-1),
					},
				],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 1 }, result.ToArray());
		}

		[TestMethod]
		public void NameContainsAndColumnConditions_AreBothApplied()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt", extension: ".txt"),
				CreateItem(2, "report.png", extension: ".png"),
			};
			var spec = new DirectoryFilterSpec
			{
				NameContains = "report",
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Extension,
					Values = [".png"],
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			CollectionAssert.AreEqual(new long[] { 2 }, result.ToArray());
		}

		[TestMethod]
		[DataRow(DirectoryTextOperator.StartsWithNot, "annual", true)]
		[DataRow(DirectoryTextOperator.StartsWithNot, "rep", false)]
		[DataRow(DirectoryTextOperator.EndsWithNot, ".txt", false)]
		[DataRow(DirectoryTextOperator.NotEquals, "REPORT.TXT", false)]
		public void NewTextOperators_CompareAgainstDisplayName(DirectoryTextOperator textOperator, string text, bool expected)
		{
			var items = new[] { CreateItem(1, "report.txt") };
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = textOperator,
					Text = text,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(expected ? 1 : 0, result.Count);
		}

		[TestMethod]
		[DataRow(DirectoryTextOperator.LengthGreaterThan, 5, true)]
		[DataRow(DirectoryTextOperator.LengthLessThan, 7, true)]
		[DataRow(DirectoryTextOperator.LengthEquals, 6, true)]
		[DataRow(DirectoryTextOperator.LengthNotEquals, 6, false)]
		public void LengthOperators_UseDisplayNameCharacterCount(DirectoryTextOperator textOperator, int length, bool expected)
		{
			var items = new[] { CreateItem(1, "report.txt", displayName: "report") };
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = textOperator,
					Length = length,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(expected ? 1 : 0, result.Count);
		}

		[TestMethod]
		public void LengthOperator_WithoutLength_MatchesEverything()
		{
			var items = new[]
			{
				CreateItem(1, "report.txt"),
				CreateItem(2, "a.txt"),
			};
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = DirectoryTextOperator.LengthEquals,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(2, result.Count);
		}

		[TestMethod]
		[DataRow("*.png", "首页-视觉稿.png", true)]
		[DataRow("*.png", "logo.png.bak", false)]
		[DataRow("*.PNG", "x.png", true)]
		[DataRow("?.md", "a.md", true)]
		[DataRow("?.md", "ab.md", false)]
		public void WildcardOperator_MatchesWholeDisplayName(string pattern, string displayName, bool expected)
		{
			var items = new[] { CreateItem(1, displayName, displayName: displayName) };
			var spec = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = DirectoryTextOperator.Wildcard,
					Text = pattern,
				}],
			};

			var result = DirectoryFilterEvaluator.Evaluate(items, spec);

			Assert.AreEqual(expected ? 1 : 0, result.Count);
		}

		[TestMethod]
		public void FromKeywordText_SplitsOnSpaceAndCommas()
		{
			var textSpec = DirectoryFilterSpec.FromKeywordText("报告, 2026 目标");

			Assert.AreEqual(3, textSpec.Columns.Count);
			CollectionAssert.AreEqual(
				new[] { "报告", "2026", "目标" },
				textSpec.Columns.Select(column => column.Text).ToArray());
			Assert.IsTrue(textSpec.Columns.All(column => column.Column == DirectoryColumnKind.Name));
			Assert.IsTrue(textSpec.Columns.All(column => column.Operator == DirectoryTextOperator.Contains));

			var wildcardSpec = DirectoryFilterSpec.FromKeywordText("*.png");

			Assert.AreEqual(1, wildcardSpec.Columns.Count);
			Assert.AreEqual(DirectoryColumnKind.Name, wildcardSpec.Columns[0].Column);
			Assert.AreEqual(DirectoryTextOperator.Wildcard, wildcardSpec.Columns[0].Operator);
			Assert.AreEqual("*.png", wildcardSpec.Columns[0].Text);

			foreach (var blank in new string?[] { null, "", "  ,，  " })
				Assert.IsTrue(DirectoryFilterSpec.FromKeywordText(blank).IsEmpty);
		}

		private static DirectoryItemFilterSnapshot CreateItem(
			long id,
			string rawName,
			string? displayName = null,
			string? itemType = null,
			string? extension = null,
			IReadOnlyList<string>? tags = null,
			long? size = 0,
			DateTimeOffset? modified = null,
			DateTimeOffset? created = null,
			bool isFolder = false)
		{
			return new DirectoryItemFilterSnapshot(
				id,
				rawName,
				displayName ?? rawName,
				@$"C:\Items\{rawName}",
				extension,
				itemType,
				tags ?? [],
				size,
				modified,
				created,
				isFolder);
		}
	}
}
