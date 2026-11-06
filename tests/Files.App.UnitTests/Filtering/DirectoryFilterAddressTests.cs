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
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
			Assert.AreEqual(@"C:\Work", parsed.Path);
			Assert.AreEqual("log", parsed.Filter.NameContains);
			Assert.AreEqual(6, parsed.Filter.Columns.Count);
			Assert.AreEqual(DirectoryColumnKind.Type, parsed.Filter.Columns[0].Column);
			CollectionAssert.AreEqual(new[] { "Text Document" }, parsed.Filter.Columns[0].Values.ToArray());
			Assert.AreEqual(10L * 1024 * 1024, parsed.Filter.Columns[3].MinSize);
			Assert.AreEqual(100L * 1024 * 1024, parsed.Filter.Columns[3].MaxSize);
			Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), parsed.Filter.Columns[4].MinDate);
		}

		[TestMethod]
		public void RoundTrip_QuotedValuesWithSeparatorsAndChinese()
		{
			var filter = new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Text = "a;b&c,\"日志\"", Operator = DirectoryTextOperator.Equals }],
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out var error), error.ToString());
			Assert.AreEqual("a;b&c,\"日志\"", parsed.Filter.Columns[0].Text);
		}

		[TestMethod]
		public void RoundTrip_Regex()
		{
			var filter = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions("^a;b$", DirectoryRegexTarget.FullPath, false),
			};

			var text = DirectoryAddressCodec.Format(@"C:\Work", filter, DirectoryViewOverrides.Empty);
			Assert.IsTrue(DirectoryAddressCodec.TryParse(text, out var parsed, out _));
			Assert.AreEqual(filter.Regex, parsed.Filter.Regex);
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
		public void TryParse_EmptyOnSize_ReturnsError()
		{
			Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=empty=size", out _, out var error));
			Assert.AreEqual(DirectoryAddressError.UnsupportedEmptyColumn, error);
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
			Assert.IsTrue(DirectoryAddressCodec.TryParse(@"C:\Work ?future=1&filter=name contains ""a""", out var address, out _));
			Assert.AreEqual("a", address.Filter.NameContains);
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
			Assert.IsFalse(DirectoryAddressCodec.TryParse(@"C:\Work ?filter=name contains """"", out _, out var error));
			Assert.AreEqual(DirectoryAddressError.MissingValue, error);
		}

		[TestMethod]
		public void GetPathPart_StripsParameters()
		{
			Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work ?filter=name contains ""a"""));
			Assert.AreEqual(@"C:\Work", DirectoryAddressCodec.GetPathPart(@"C:\Work"));
		}
	}
}
