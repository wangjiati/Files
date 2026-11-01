// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenameExecutionPlanTests
	{
		private static readonly DateTimeOffset CreatedUtc = new(2026, 11, 1, 9, 5, 7, TimeSpan.Zero);
		private static readonly DateTimeOffset ModifiedUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

		private static RenameTarget Target(string folder, string name, bool isFolder = false)
		{
			return new RenameTarget($"{folder}\\{name}", name, isFolder, CreatedUtc, ModifiedUtc, "父");
		}

		private static RenameOutcome Changed(string original, string proposed)
		{
			return new RenameOutcome(original, proposed, RenameOutcomeKind.Changed, RenameError.None);
		}

		private static RenameOutcome Unchanged(string name)
		{
			return new RenameOutcome(name, name, RenameOutcomeKind.Unchanged, RenameError.None);
		}

		private static RenameOutcome Invalid(string name, string candidate)
		{
			return new RenameOutcome(name, candidate, RenameOutcomeKind.RuleError, RenameError.InvalidName);
		}

		private static RenamePreviewRow Row(
			RenameTarget target,
			RenameOutcome outcome,
			ConflictStatus conflictStatus = ConflictStatus.None,
			string? conflictFinalName = null,
			bool isIncluded = true,
			bool includeFolders = true,
			string? manualName = null)
		{
			var row = new RenamePreviewRow(
				target,
				outcome,
				new ConflictOutcome(target.FullPath, conflictFinalName ?? outcome.NewName, conflictStatus),
				includeFolders)
			{
				IsIncluded = isIncluded,
			};
			if (manualName is not null)
				row.SetManual(manualName);

			return row;
		}

		[TestMethod]
		public void BuildPlan_ExcludesUnchangedUncheckedSkippedAndErrorRows()
		{
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\pic", "same.jpg"), Unchanged("same.jpg")),
				Row(Target(@"C:\pic", "two.jpg"), Changed("two.jpg", "新_2.jpg")),
				Row(Target(@"C:\pic", "three.jpg"), Changed("three.jpg", "新_3.jpg"), isIncluded: false),
				Row(Target(@"C:\pic", "four.jpg"), Changed("four.jpg", "占位.jpg"), ConflictStatus.WillSkip, "four.jpg"),
				Row(Target(@"C:\pic", "five.jpg"), Invalid("five.jpg", "bad<name")),
				Row(Target(@"C:\pic", "six.jpg"), Changed("six.jpg", "手改_6.jpg"), manualName: "手改_6.jpg"),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(2, plan.Count);
			Assert.AreEqual(rows[1].Target.FullPath, plan[0].SourcePath);
			Assert.AreEqual(rows[5].Target.FullPath, plan[1].SourcePath);
		}

		[TestMethod]
		public void BuildPlan_KeepsListOrderForUnchainedRows()
		{
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\pic", "a.jpg"), Changed("a.jpg", "甲.jpg")),
				Row(Target(@"C:\other", "b.jpg"), Changed("b.jpg", "乙.jpg")),
				Row(Target(@"C:\pic", "c.jpg"), Changed("c.jpg", "丙.jpg")),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(3, plan.Count);
			Assert.AreEqual(rows[0].Target.FullPath, plan[0].SourcePath);
			Assert.AreEqual(rows[1].Target.FullPath, plan[1].SourcePath);
			Assert.AreEqual(rows[2].Target.FullPath, plan[2].SourcePath);
		}

		[TestMethod]
		public void BuildPlan_SequencedRow_CarriesNumberedDestination()
		{
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\pic", "first.jpg"), Changed("first.jpg", "京都_2053.jpg")),
				Row(Target(@"C:\pic", "second.jpg"), Changed("second.jpg", "京都_2053.jpg"), ConflictStatus.WillAppendNumber, "京都_2053 (2).jpg"),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(2, plan.Count);
			Assert.IsTrue(plan[1].DestinationPath.EndsWith(@"\京都_2053 (2).jpg", StringComparison.Ordinal));
			Assert.AreEqual(ConflictStatus.WillAppendNumber, plan[1].Status);
		}

		[TestMethod]
		public void BuildPlan_ChainedRenames_ReorderedSoReverseReplayIsSafe()
		{
			// Renaming a→b requires b to be vacated first, so (b→c) must run before (a→b);
			// undoing afterwards in reverse order restores both names without intermediate conflicts.
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\t", "a.txt"), Changed("a.txt", "b.txt")),
				Row(Target(@"C:\t", "b.txt"), Changed("b.txt", "c.txt")),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(2, plan.Count);

			// The plan contains both source/destination pairs, in reverse replay order.
			Assert.AreEqual(@"C:\t\b.txt", plan[0].SourcePath);
			Assert.AreEqual(@"C:\t\c.txt", plan[0].DestinationPath);
			Assert.AreEqual(@"C:\t\a.txt", plan[1].SourcePath);
			Assert.AreEqual(@"C:\t\b.txt", plan[1].DestinationPath);

			// Forward execution: every source is present when its rename runs.
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.txt", "b.txt" };
			foreach (RenameExecutionItem item in plan)
			{
				string source = Path.GetFileName(item.SourcePath);
				string destination = Path.GetFileName(item.DestinationPath);
				Assert.IsTrue(names.Remove(source), $"{source} missing at execution time");
				names.Add(destination);
			}

			// Reverse replay (undo): every renamed name is present when it is restored.
			foreach (RenameExecutionItem item in plan.Reverse())
			{
				string source = Path.GetFileName(item.SourcePath);
				string destination = Path.GetFileName(item.DestinationPath);
				Assert.IsTrue(names.Remove(destination), $"{destination} missing at undo time");
				names.Add(source);
			}

			CollectionAssert.AreEquivalent(new[] { "a.txt", "b.txt" }, names.ToList());
		}

		[TestMethod]
		public void BuildPlan_SwapCycle_TerminatesAndKeepsBothRows()
		{
			// A pure swap (a→b, b→a) has no dependency-safe order; the builder must still
			// terminate and return both items in input order, letting execution report the failure.
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\t", "a.txt"), Changed("a.txt", "b.txt")),
				Row(Target(@"C:\t", "b.txt"), Changed("b.txt", "a.txt")),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(2, plan.Count);
			Assert.AreEqual(@"C:\t\a.txt", plan[0].SourcePath);
			Assert.AreEqual(@"C:\t\b.txt", plan[1].SourcePath);
		}

		[TestMethod]
		public void BuildPlan_ManualNameEqualToOriginal_IsExcludedAsNoOp()
		{
			var rows = new RenamePreviewRow[]
			{
				Row(Target(@"C:\pic", "a.jpg"), Changed("a.jpg", "甲.jpg"), manualName: "a.jpg"),
				Row(Target(@"C:\pic", "b.jpg"), Changed("b.jpg", "乙.jpg"), manualName: "B.jpg"),
			};

			var plan = RenameExecutor.BuildExecutionPlan(rows, ConflictStrategy.AppendNumber);

			Assert.AreEqual(1, plan.Count);
			Assert.AreEqual(rows[1].Target.FullPath, plan[0].SourcePath);
			Assert.IsTrue(plan[0].DestinationPath.EndsWith(@"\B.jpg", StringComparison.Ordinal));
		}
	}
}
