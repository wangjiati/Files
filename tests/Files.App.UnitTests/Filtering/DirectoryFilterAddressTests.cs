// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryFilterAddressTests
	{
		[TestMethod]
		public void TryParse_NoParameters_ReturnsPlainPath()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work", out var address, out _));
			Assert.AreEqual(@"C:\Work", address.Path);
			Assert.IsTrue(address.Filter.IsEmpty);
			Assert.IsTrue(address.View.IsEmpty);
		}

		[TestMethod]
		public void Format_UsesLinqExpression()
		{
			var filter = new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Text = "report" },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 1024 * 1024 },
				],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);

			StringAssert.Contains(text, "filter=Name.Contains(\"report\")");
			StringAssert.Contains(text, "&& Size >= 1MB");
			Assert.IsFalse(text.Contains("contains \"report\"", StringComparison.Ordinal));
		}

		[TestMethod]
		public void RoundTrip_AllFilterFields()
		{
			var filter = new DirectoryFilterSpec
			{
				NameContains = "log",
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["Text Document"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".txt"], IncludeEmpty = true },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Tags, Values = ["uid-1"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 10 * 1024 * 1024, MaxSize = 100 * 1024 * 1024 },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.ModifiedDate, MinDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.CreatedDate, MaxDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) },
				],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), text + " :: " + error);
			Assert.AreEqual(@"C:\Work", parsed.Path);
			Assert.IsNull(parsed.Filter.NameContains);
			Assert.AreEqual(7, parsed.Filter.Columns.Count);

			Assert.AreEqual(DirectoryColumnKind.Name, parsed.Filter.Columns[0].Column);
			Assert.AreEqual(DirectoryTextOperator.Contains, parsed.Filter.Columns[0].Operator);
			Assert.AreEqual("log", parsed.Filter.Columns[0].Text);

			Assert.AreEqual(DirectoryColumnKind.Type, parsed.Filter.Columns[1].Column);
			CollectionAssert.AreEqual(new[] { "Text Document" }, parsed.Filter.Columns[1].Values.ToArray());

			Assert.AreEqual(DirectoryColumnKind.Extension, parsed.Filter.Columns[2].Column);
			CollectionAssert.AreEqual(new[] { ".txt" }, parsed.Filter.Columns[2].Values.ToArray());
			Assert.IsTrue(parsed.Filter.Columns[2].IncludeEmpty);

			Assert.AreEqual(DirectoryColumnKind.Tags, parsed.Filter.Columns[3].Column);
			CollectionAssert.AreEqual(new[] { "uid-1" }, parsed.Filter.Columns[3].Values.ToArray());

			Assert.AreEqual(DirectoryColumnKind.Size, parsed.Filter.Columns[4].Column);
			Assert.AreEqual(10L * 1024 * 1024, parsed.Filter.Columns[4].MinSize);
			Assert.AreEqual(100L * 1024 * 1024, parsed.Filter.Columns[4].MaxSize);

			Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), parsed.Filter.Columns[5].MinDate);
			Assert.AreEqual(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), parsed.Filter.Columns[6].MaxDate);
		}

		[TestMethod]
		public void RoundTrip_QuotedValuesWithSeparatorsAndChinese()
		{
			var filter = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Text = "a;b&c,\"日志\"", Operator = DirectoryTextOperator.Equals }],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), text + " :: " + error);
			Assert.AreEqual("a;b&c,\"日志\"", parsed.Filter.Columns[0].Text);
			Assert.AreEqual(DirectoryTextOperator.Equals, parsed.Filter.Columns[0].Operator);
		}

		[TestMethod]
		public void RoundTrip_Regex()
		{
			var filter = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions("^a;b$", DirectoryRegexTarget.FullPath, false),
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			StringAssert.Contains(text, "Regex.IsMatch(FullPath, \"^a;b$\", \"c\")");
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out _));
			Assert.AreEqual(filter.Regex, parsed.Filter.Regex);
		}

		[TestMethod]
		public void RoundTrip_NegatedTextAndWildcard()
		{
			var filter = new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Operator = DirectoryTextOperator.NotContains, Text = "tmp" },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Operator = DirectoryTextOperator.Wildcard, Scope = DirectoryNameScope.RawName, Text = "*.zip" },
				],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			StringAssert.Contains(text, "!Name.Contains(\"tmp\")");
			StringAssert.Contains(text, "RawName.Wildcard(\"*.zip\")");

			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
			Assert.AreEqual(DirectoryTextOperator.NotContains, parsed.Filter.Columns[0].Operator);
			Assert.AreEqual(DirectoryTextOperator.Wildcard, parsed.Filter.Columns[1].Operator);
			Assert.AreEqual(DirectoryNameScope.RawName, parsed.Filter.Columns[1].Scope);
		}

		[TestMethod]
		public void RoundTrip_ChoiceValuesWithOr()
		{
			var filter = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["File", "Folder"] }],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			StringAssert.Contains(text, "(Type == \"File\" || Type == \"Folder\")");

			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
			Assert.AreEqual(1, parsed.Filter.Columns.Count);
			CollectionAssert.AreEqual(new[] { "File", "Folder" }, parsed.Filter.Columns[0].Values.ToArray());
		}

		[TestMethod]
		public void RoundTrip_ViewOverrides()
		{
			var view = new DirectoryViewOverrides
			{
				GroupOption = GroupOption.DateModified,
				GroupDirection = SortDirection.Descending,
				GroupByDateUnit = GroupByDateUnit.Month,
				SortOption = SortOption.Size,
				SortDirection = SortDirection.Ascending,
				LayoutMode = FolderLayoutModes.ListView,
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", DirectoryFilterSpec.Empty, view);
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
			Assert.AreEqual(view, parsed.View);
		}

		[TestMethod]
		public void TryParse_GroupWithoutDirection_LeavesDirectionNull()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?group=size", out var address, out _));
			Assert.AreEqual(GroupOption.Size, address.View.GroupOption);
			Assert.IsNull(address.View.GroupDirection);
		}

		[TestMethod]
		public void TryParse_EmptyChoiceValue_SetsIncludeEmpty()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=Extension == """"", out var address, out var error), error.ToString());
			var column = address.Filter.Columns.Single();
			Assert.AreEqual(DirectoryColumnKind.Extension, column.Column);
			Assert.IsTrue(column.IncludeEmpty);
		}

		[TestMethod]
		public void TryParse_DevicePath_IsTreatedAsPlainPath()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"\\?\C:\Work", out var address, out _));
			Assert.AreEqual(@"\\?\C:\Work", address.Path);
			Assert.IsTrue(address.Filter.IsEmpty);
		}

		[TestMethod]
		public void TryParse_UnknownKey_IsIgnored()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?future=1&filter=Name.Contains(""a"")", out var address, out _));
			Assert.AreEqual("a", address.Filter.Columns.Single().Text);
		}

		[TestMethod]
		public void TryParse_InvalidClause_ReturnsError()
		{
			Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=size >> 10MB", out _, out var error));
			Assert.AreEqual(DirectoryAddressError.InvalidOperator, error);
		}

		[TestMethod]
		public void RoundTrip_SizeBoundaries()
		{
			foreach (var bytes in new[] { 1500L, 1_048_575L, 1_059_062L })
			{
				var filter = new DirectoryFilterSpec
				{
					Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = bytes, MaxSize = bytes }],
				};

				var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
				Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
				Assert.AreEqual(bytes, parsed.Filter.Columns[0].MinSize, text);
				Assert.AreEqual(bytes, parsed.Filter.Columns[0].MaxSize, text);
			}
		}

		[TestMethod]
		public void TryParse_FtpPathWithQuestionMark_IsTreatedAsPlainPath()
		{
			const string path = "ftp://host/My ?filter=files";
			Assert.IsTrue(DirectoryAddressCodec.TryParse(path, out var address, out _));
			Assert.AreEqual(path, address.Path);
			Assert.IsTrue(address.Filter.IsEmpty);
			Assert.AreEqual(path, DirectoryAddressCodec.GetPathPart(path));
		}

		[TestMethod]
		public void TryParse_EmptyNameValue_ReturnsError()
		{
			Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=Name.Contains("""")", out _, out var error));
			Assert.AreEqual(DirectoryAddressError.MissingValue, error);
		}

		[TestMethod]
		public void TryParse_AmpersandsStayInsideTheFilter()
		{
			Assert.IsTrue(DirectoryAddressCodec.TryParse(
				@"C:\Work ?filter=Name.Contains(""a"") && Size >= 1MB&group=size",
				out var address,
				out var error), error.ToString());

			Assert.AreEqual(2, address.Filter.Columns.Count);
			Assert.AreEqual(DirectoryColumnKind.Name, address.Filter.Columns[0].Column);
			Assert.AreEqual(DirectoryColumnKind.Size, address.Filter.Columns[1].Column);
			Assert.AreEqual(GroupOption.Size, address.View.GroupOption);
		}

		[TestMethod]
		public void TryParse_UnsupportedGroup_ReturnsError()
		{
			Assert.IsFalse(DirectoryAddressCodec.TryParse(
				@"C:\Work ?filter=(Name.Contains(""a"") && Size >= 1MB) || Type == ""x""",
				out _,
				out var error));
			Assert.AreEqual(DirectoryAddressError.InvalidValue, error);
		}

		[TestMethod]
		public void GetPathPart_StripsParameters()
		{
			Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work ?filter=Name.Contains(""a"")"));
			Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work"));
		}
	}
}
