// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenameVariableExpanderTests
	{
		private static VariableContext CreateContext(FileDateSource source = FileDateSource.Created)
		{
			return new VariableContext(
				Created: new DateTimeOffset(2026, 11, 1, 9, 5, 7, 123, TimeSpan.Zero),
				Modified: new DateTimeOffset(2027, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
				Source: source,
				FolderName: "夜景");
		}

		[TestMethod]
		public void Counter_Default_ProducesSequentialNumbers()
		{
			var expander = RenameVariableExpander.Create("照片_${}.jpg");

			Assert.AreEqual("照片_0.jpg", expander.Expand(CreateContext()));
			Assert.AreEqual("照片_1.jpg", expander.Expand(CreateContext()));
			Assert.AreEqual("照片_2.jpg", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void Counter_WithOptions_UsesStartPaddingAndIncrement()
		{
			var expander = RenameVariableExpander.Create("${start=10;padding=4;increment=2}");

			Assert.AreEqual("0010", expander.Expand(CreateContext()));
			Assert.AreEqual("0012", expander.Expand(CreateContext()));
			Assert.AreEqual("0014", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void Counter_MultipleInSamePattern_AdvanceIndependently()
		{
			var expander = RenameVariableExpander.Create("A${}B${padding=2}");

			Assert.AreEqual("A0B00", expander.Expand(CreateContext()));
			Assert.AreEqual("A1B01", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void Date_CreatedSource_FormatsCreatedTimestamp()
		{
			var expander = RenameVariableExpander.Create("$YYYY-$MM-$DD_$hh$mm$ss");

			Assert.AreEqual("2026-11-01_090507", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void Date_Milliseconds_FormatsThreeDigits()
		{
			var expander = RenameVariableExpander.Create("$fff");

			Assert.AreEqual("123", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void Date_ModifiedSource_ReadsModifiedTimestamp()
		{
			var expander = RenameVariableExpander.Create("$YYYY-$MM-$DD_$hh$mm$ss$fff");

			Assert.AreEqual("2027-01-02_030405678", expander.Expand(CreateContext(FileDateSource.Modified)));
		}

		[TestMethod]
		public void FolderName_AppendsParentFolderName()
		{
			var expander = RenameVariableExpander.Create("${folder}");

			Assert.AreEqual("夜景", expander.Expand(CreateContext()));
		}

		[TestMethod]
		public void MalformedVariables_AreKeptLiteral()
		{
			var expander = RenameVariableExpander.Create("${abc}$YYY");

			Assert.AreEqual("${abc}$YYY", expander.Expand(CreateContext()));
		}
	}
}
