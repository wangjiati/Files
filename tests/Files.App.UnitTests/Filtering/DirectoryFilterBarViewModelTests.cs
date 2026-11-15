// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Files.App.ViewModels.UserControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryFilterBarViewModelTests
	{
		[TestMethod]
		public void BuildSpec_BasicMode_ParsesKeywordText()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "报告, 2026",
			};

			var spec = viewModel.BuildSpec();

			Assert.AreEqual(2, spec.Columns.Count);
			Assert.IsTrue(spec.Columns.All(column => column.Column is DirectoryColumnKind.Name));
			CollectionAssert.AreEqual(new[] { "报告", "2026" }, spec.Columns.Select(column => column.Text).ToArray());
		}

		[TestMethod]
		public void BuildSpec_AdvancedMode_UsesConditionItemsWithJoins()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "ignored",
				Mode = FilterMode.Advanced,
			};
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.And);
			viewModel.AddCondition(DirectoryColumnKind.Extension, DirectoryConditionJoin.Or);
			viewModel.Conditions[0].Text = "report";
			viewModel.Conditions[1].LoadChoices(null, [new DirectoryFilterChoice(".txt", ".txt")]);
			viewModel.Conditions[1].ExtensionChoices[0].IsSelected = true;

			var spec = viewModel.BuildSpec();

			Assert.IsNull(spec.NameContains);
			Assert.AreEqual(2, spec.Columns.Count);
			Assert.AreEqual("report", spec.Columns[0].Text);
			Assert.AreEqual(DirectoryConditionJoin.Or, spec.Columns[1].Join);
			CollectionAssert.AreEqual(new[] { ".txt" }, spec.Columns[1].Values.ToArray());
		}

		[TestMethod]
		public void BuildSpec_AdvancedMode_SkipsBlankDraftConditions()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.And);

			var spec = viewModel.BuildSpec();

			Assert.AreEqual(1, viewModel.Conditions.Count);
			Assert.AreEqual(0, spec.Columns.Count);
			Assert.IsTrue(spec.IsEmpty);
		}

		[TestMethod]
		public void BuildSpec_AdvancedMode_OrDraftDoesNotDisableRealConditions()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.And);
			viewModel.Conditions[0].Text = "report";
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.Or);

			var spec = viewModel.BuildSpec();

			Assert.AreEqual(1, spec.Columns.Count);
			Assert.AreEqual("report", spec.Columns[0].Text);

			var items = new[]
			{
				new DirectoryItemFilterSnapshot(1, "report.txt", "report.txt", @"C:\report.txt", ".txt", null, [], 0, null, null, false),
				new DirectoryItemFilterSnapshot(2, "other.txt", "other.txt", @"C:\other.txt", ".txt", null, [], 0, null, null, false),
			};

			CollectionAssert.AreEqual(new long[] { 1 }, DirectoryFilterEvaluator.Evaluate(items, spec).ToArray());
		}

		[TestMethod]
		public void BuildSpec_AdvancedMode_IncludesActiveNameCondition()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.NameCondition.Text = "report";
			viewModel.NameCondition.OperatorIndex = (int)DirectoryTextOperator.StartsWith;
			viewModel.AddCondition(DirectoryColumnKind.Size, DirectoryConditionJoin.Or);
			viewModel.Conditions[0].MinSizeText = "1";

			var spec = viewModel.BuildSpec();

			Assert.AreEqual(2, spec.Columns.Count);
			Assert.AreEqual("report", spec.Columns[0].Text);
			Assert.AreEqual(DirectoryTextOperator.StartsWith, spec.Columns[0].Operator);
			Assert.AreEqual(DirectoryConditionJoin.Or, spec.Columns[1].Join);
		}

		[TestMethod]
		public void HasActiveFilter_IsTrueForNameConditionOnly()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};

			Assert.IsFalse(viewModel.HasActiveFilter);

			viewModel.NameCondition.Text = "report";
			Assert.IsTrue(viewModel.HasActiveFilter);
		}

		[TestMethod]
		public void HasActiveFilter_IsTrueForConditionChainOnly()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};

			viewModel.AddCondition(DirectoryColumnKind.Size, DirectoryConditionJoin.And);
			Assert.IsFalse(viewModel.HasActiveFilter);

			viewModel.Conditions[0].MinSizeText = "1";
			Assert.IsTrue(viewModel.HasActiveFilter);
		}

		[TestMethod]
		public void AddCondition_LoadsChoicesFromProvider()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				ChoiceProvider = column => column is DirectoryColumnKind.Extension
					? [new DirectoryFilterChoice(".txt", ".txt")]
					: [new DirectoryFilterChoice("Text Document", "Text Document")],
			};

			viewModel.AddCondition(DirectoryColumnKind.Type, DirectoryConditionJoin.And);
			viewModel.AddCondition(DirectoryColumnKind.Extension, DirectoryConditionJoin.And);
			viewModel.AddCondition(DirectoryColumnKind.Size, DirectoryConditionJoin.And);

			Assert.AreEqual("Text Document", viewModel.Conditions[0].Choices.Single().Value);
			Assert.AreEqual(".txt", viewModel.Conditions[1].ExtensionChoices.Single().Value);
			Assert.AreEqual(0, viewModel.Conditions[2].Choices.Count);
		}

		[TestMethod]
		public void ModeSwitch_PreservesBothConfigurations()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "report",
			};
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.And);
			viewModel.Conditions[0].Text = "annual";

			Assert.AreEqual("report", viewModel.BuildSpec().Columns.Single().Text);

			viewModel.Mode = FilterMode.Advanced;
			Assert.AreEqual("annual", viewModel.BuildSpec().Columns.Single().Text);

			viewModel.Mode = FilterMode.Basic;
			Assert.AreEqual("report", viewModel.KeywordText);
			Assert.AreEqual(1, viewModel.Conditions.Count);
			Assert.AreEqual("annual", viewModel.Conditions[0].Text);
			Assert.AreEqual("report", viewModel.BuildSpec().Columns.Single().Text);

			viewModel.Mode = FilterMode.Advanced;
			Assert.AreEqual("annual", viewModel.BuildSpec().Columns.Single().Text);
		}

		[TestMethod]
		[DataRow(0, 1)]
		[DataRow(999, 64)]
		public void RecursiveDepth_IsClamped(int requested, int expected)
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				RecursiveDepth = requested,
			};

			Assert.AreEqual(expected, viewModel.RecursiveDepth);
			Assert.AreEqual(2, new DirectoryFilterBarViewModel().RecursiveDepth);
		}

		[TestMethod]
		public void ClearAll_ResetsKeywordsAndConditions_ButKeepsRecursiveScope()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "report",
				RecursiveEnabled = true,
				RecursiveDepth = 5,
			};
			viewModel.AddCondition(DirectoryColumnKind.Size, DirectoryConditionJoin.And);

			viewModel.ClearAll();

			Assert.IsTrue(string.IsNullOrEmpty(viewModel.KeywordText));
			Assert.AreEqual(0, viewModel.Conditions.Count);
			Assert.IsTrue(viewModel.RecursiveEnabled);
			Assert.AreEqual(5, viewModel.RecursiveDepth);
			Assert.IsTrue(viewModel.BuildSpec().IsEmpty);
		}

		[TestMethod]
		public void SpecChanged_IsRaisedOnAnyMutation()
		{
			var viewModel = new DirectoryFilterBarViewModel();
			var raised = 0;
			viewModel.SpecChanged += (_, _) => raised++;

			void AssertRaised(Action mutation)
			{
				var before = raised;
				mutation();
				Assert.IsTrue(raised > before, "SpecChanged was not raised.");
			}

			AssertRaised(() => viewModel.KeywordText = "report");
			AssertRaised(() => viewModel.Mode = FilterMode.Advanced);
			AssertRaised(() => viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.Or));
			AssertRaised(() => viewModel.Conditions[0].Text = "annual");
			AssertRaised(() => viewModel.Conditions[0].Join = DirectoryConditionJoin.And);
			AssertRaised(() => viewModel.RemoveCondition(viewModel.Conditions[0]));
			AssertRaised(() => viewModel.RecursiveEnabled = true);
			AssertRaised(() => viewModel.RecursiveDepth = 3);
			AssertRaised(viewModel.ClearAll);
		}
	}
}
