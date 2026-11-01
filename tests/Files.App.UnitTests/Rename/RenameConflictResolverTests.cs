// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenameConflictResolverTests
	{
		private static ConflictInput Row(string id, string folderPath, string originalName, string proposedName, params string[] existingNames)
		{
			return new ConflictInput(id, folderPath, originalName, proposedName, new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase));
		}

		[TestMethod]
		public void Resolve_SameFolderTwoRows_AppendNumber_SecondRowGetsNumberSuffix()
		{
			var rows = new[]
			{
				Row("a", @"C:\pic", "IMG_1.jpg", "京都_2053.jpg"),
				Row("b", @"C:\pic", "IMG_2.jpg", "京都_2053.jpg"),
			};

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(2, outcomes.Count);
			Assert.AreEqual("a", outcomes[0].Id);
			Assert.AreEqual(ConflictStatus.None, outcomes[0].Status);
			Assert.AreEqual("京都_2053.jpg", outcomes[0].FinalName);
			Assert.AreEqual("b", outcomes[1].Id);
			Assert.AreEqual(ConflictStatus.WillAppendNumber, outcomes[1].Status);
			Assert.AreEqual("京都_2053 (2).jpg", outcomes[1].FinalName);
		}

		[TestMethod]
		public void Resolve_SameFolderTwoRows_Skip_SecondRowKeepsOriginalName()
		{
			var rows = new[]
			{
				Row("a", @"C:\pic", "IMG_1.jpg", "京都_2053.jpg"),
				Row("b", @"C:\pic", "IMG_2.jpg", "京都_2053.jpg"),
			};

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.Skip);

			Assert.AreEqual(ConflictStatus.None, outcomes[0].Status);
			Assert.AreEqual("京都_2053.jpg", outcomes[0].FinalName);
			Assert.AreEqual(ConflictStatus.WillSkip, outcomes[1].Status);
			Assert.AreEqual("IMG_2.jpg", outcomes[1].FinalName);
		}

		[TestMethod]
		public void Resolve_SameFolderTwoRows_Overwrite_SecondRowWillOverwrite()
		{
			var rows = new[]
			{
				Row("a", @"C:\pic", "IMG_1.jpg", "京都_2053.jpg"),
				Row("b", @"C:\pic", "IMG_2.jpg", "京都_2053.jpg"),
			};

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.Overwrite);

			Assert.AreEqual(ConflictStatus.None, outcomes[0].Status);
			Assert.AreEqual("京都_2053.jpg", outcomes[0].FinalName);
			Assert.AreEqual(ConflictStatus.WillOverwrite, outcomes[1].Status);
			Assert.AreEqual("京都_2053.jpg", outcomes[1].FinalName);
		}

		[TestMethod]
		public void Resolve_ProposedNameMatchesExistingFile_AppendNumber_InsertsNumberBeforeExtension()
		{
			var rows = new[] { Row("a", @"C:\pic", "old.jpg", "名称.ext", "名称.ext") };

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(ConflictStatus.WillAppendNumber, outcomes[0].Status);
			Assert.AreEqual("名称 (2).ext", outcomes[0].FinalName);
		}

		[TestMethod]
		public void Resolve_ProposedNameMatchesExistingFile_Overwrite_ReportsWillOverwrite()
		{
			var rows = new[] { Row("a", @"C:\pic", "old.jpg", "名称.ext", "名称.ext") };

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.Overwrite);

			Assert.AreEqual(ConflictStatus.WillOverwrite, outcomes[0].Status);
			Assert.AreEqual("名称.ext", outcomes[0].FinalName);
		}

		[TestMethod]
		public void Resolve_CaseOnlyRename_StatusNone()
		{
			var rows = new[] { Row("a", @"C:\pic", "IMG_2053.jpg", "img_2053.jpg", "IMG_2053.jpg") };

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(ConflictStatus.None, outcomes[0].Status);
			Assert.AreEqual("img_2053.jpg", outcomes[0].FinalName);
		}

		[TestMethod]
		public void Resolve_AppendNumberWhenNumberTaken_ContinuesToNextNumber()
		{
			var rows = new[] { Row("a", @"C:\pic", "old.jpg", "名称.ext", "名称.ext", "名称 (2).ext") };

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(ConflictStatus.WillAppendNumber, outcomes[0].Status);
			Assert.AreEqual("名称 (3).ext", outcomes[0].FinalName);
		}

		[TestMethod]
		public void Resolve_SameProposedNameInDifferentFolders_AreIndependent()
		{
			var rows = new[]
			{
				Row("a", @"C:\pic", "old1.jpg", "report.pdf", "report.pdf"),
				Row("b", @"D:\doc", "old2.jpg", "report.pdf"),
			};

			var outcomes = RenameConflictResolver.Resolve(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(ConflictStatus.WillAppendNumber, outcomes[0].Status);
			Assert.AreEqual("report (2).pdf", outcomes[0].FinalName);
			Assert.AreEqual(ConflictStatus.None, outcomes[1].Status);
			Assert.AreEqual("report.pdf", outcomes[1].FinalName);
		}
	}
}
