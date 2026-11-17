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
			Assert.IsTrue(spec.Columns.All(column => column.Scope is DirectoryNameScope.RawName));
			CollectionAssert.AreEqual(new[] { "报告", "2026" }, spec.Columns.Select(column => column.Text).ToArray());
		}

		[TestMethod]
		public void NameCondition_DefaultsToFullFileNameScope()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};

			Assert.IsTrue(viewModel.NameCondition.UseRawName);
			Assert.AreEqual(DirectoryNameScope.RawName, viewModel.NameCondition.ToColumnFilter().Scope);
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
		public void HasActiveFilter_BasicMode_FollowsKeywordText()
		{
			var viewModel = new DirectoryFilterBarViewModel();

			Assert.IsFalse(viewModel.HasActiveFilter);

			viewModel.KeywordText = "report";
			Assert.IsTrue(viewModel.HasActiveFilter);

			viewModel.KeywordText = "  ";
			Assert.IsFalse(viewModel.HasActiveFilter);
		}

		[TestMethod]
		public void HasActiveFilter_AdvancedMode_IgnoresKeywordText()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
				KeywordText = "report",
			};

			Assert.IsFalse(viewModel.HasActiveFilter);

			viewModel.NameCondition.Text = "report";
			Assert.IsTrue(viewModel.HasActiveFilter);
		}

		[TestMethod]
		public void LoadFrom_CaseSensitiveNameSpec_SwitchesToAdvanced()
		{
			var viewModel = new DirectoryFilterBarViewModel();

			viewModel.LoadFrom(new DirectoryFilterSpec
			{
				Columns = [new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = DirectoryTextOperator.Contains,
					Text = "report",
					CaseSensitive = true,
				}],
			});

			Assert.AreEqual(FilterMode.Advanced, viewModel.Mode);
			Assert.IsTrue(viewModel.NameCondition.CaseSensitive);
			Assert.IsTrue(viewModel.BuildSpec().Columns.Single().CaseSensitive);
		}

		[TestMethod]
		public void LoadFrom_EmptySpec_ClearsBar()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "report",
				Mode = FilterMode.Advanced,
			};
			viewModel.AddCondition(DirectoryColumnKind.Size, DirectoryConditionJoin.And);
			viewModel.Conditions[0].MinSizeText = "1";

			viewModel.LoadFrom(DirectoryFilterSpec.Empty);

			Assert.IsTrue(string.IsNullOrEmpty(viewModel.KeywordText));
			Assert.AreEqual(0, viewModel.Conditions.Count);
			Assert.IsFalse(viewModel.NameCondition.IsActive);
			Assert.IsTrue(viewModel.BuildSpec().IsEmpty);
		}

		[TestMethod]
		public void LoadFrom_NameOnlySpec_KeepsBasicKeyword()
		{
			var viewModel = new DirectoryFilterBarViewModel();

			viewModel.LoadFrom(DirectoryFilterSpec.FromKeywordText("报告, 2026"));

			Assert.AreEqual(FilterMode.Basic, viewModel.Mode);
			Assert.AreEqual("报告 2026", viewModel.KeywordText);
			Assert.AreEqual(0, viewModel.Conditions.Count);

			var wildcardViewModel = new DirectoryFilterBarViewModel();
			wildcardViewModel.LoadFrom(DirectoryFilterSpec.FromKeywordText("*.png"));
			Assert.AreEqual("*.png", wildcardViewModel.KeywordText);
		}

		[TestMethod]
		public void LoadFrom_NameContains_WithSeparators_SwitchesToAdvanced()
		{
			var viewModel = new DirectoryFilterBarViewModel();

			viewModel.LoadFrom(new DirectoryFilterSpec { NameContains = "my file" });

			Assert.AreEqual(FilterMode.Advanced, viewModel.Mode);
			Assert.AreEqual("my file", viewModel.NameCondition.Text);
		}

		[TestMethod]
		public void LoadFrom_MixedSpec_SwitchesToAdvancedAndRebuildsConditions()
		{
			var spec = new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Name,
						Operator = DirectoryTextOperator.Contains,
						Text = "report",
					},
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Size,
						Join = DirectoryConditionJoin.Or,
						MinSize = 1024,
					},
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Extension,
						Join = DirectoryConditionJoin.And,
						Values = [".txt"],
					},
				],
			};

			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "stale",
				ChoiceProvider = column => column is DirectoryColumnKind.Extension
					? [new DirectoryFilterChoice(".txt", ".txt")]
					: [],
			};

			viewModel.LoadFrom(spec);

			Assert.AreEqual(FilterMode.Advanced, viewModel.Mode);
			Assert.AreEqual("report", viewModel.NameCondition.Text);
			Assert.AreEqual(2, viewModel.Conditions.Count);
			Assert.AreEqual(DirectoryColumnKind.Size, viewModel.Conditions[0].Column);
			Assert.AreEqual("1", viewModel.Conditions[0].MinSizeText);
			Assert.AreEqual(DirectoryConditionJoin.Or, viewModel.Conditions[0].Join);
			Assert.AreEqual(DirectoryColumnKind.Extension, viewModel.Conditions[1].Column);
			Assert.IsTrue(viewModel.Conditions[1].ExtensionChoices.Single().IsSelected);

			var rebuilt = viewModel.BuildSpec();
			Assert.AreEqual(3, rebuilt.Columns.Count);
			Assert.AreEqual("report", rebuilt.Columns[0].Text);
			Assert.AreEqual(1024L, rebuilt.Columns[1].MinSize);
			CollectionAssert.AreEqual(new[] { ".txt" }, rebuilt.Columns[2].Values.ToArray());
		}

		[TestMethod]
		public void LoadFrom_ChoiceSpec_KeepsValuesMissingFromProvider()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				ChoiceProvider = _ => [],
			};

			viewModel.LoadFrom(new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Type,
						Values = ["Text Document"],
					},
				],
			});

			Assert.AreEqual(FilterMode.Advanced, viewModel.Mode);
			Assert.IsTrue(viewModel.Conditions.Single().Choices.Single().IsSelected);
			Assert.IsTrue(viewModel.Conditions.Single().IsActive);
			CollectionAssert.AreEqual(new[] { "Text Document" }, viewModel.BuildSpec().Columns.Single().Values.ToArray());
		}

		[TestMethod]
		public void LoadFrom_RegexSpec_RestoresNameConditionRegex()
		{
			var viewModel = new DirectoryFilterBarViewModel();

			viewModel.LoadFrom(new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions("^a.*", DirectoryRegexTarget.RawName, false),
			});

			Assert.AreEqual(FilterMode.Advanced, viewModel.Mode);
			Assert.IsTrue(viewModel.NameCondition.UseRegex);
			Assert.AreEqual("^a.*", viewModel.NameCondition.RegexPattern);
			Assert.AreEqual((int)DirectoryRegexTarget.RawName, viewModel.NameCondition.RegexTargetIndex);
		}

		[TestMethod]
		public void LoadFrom_DoesNotRaiseSpecChanged()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				KeywordText = "report",
			};
			var raised = 0;
			viewModel.SpecChanged += (_, _) => raised++;

			viewModel.LoadFrom(DirectoryFilterSpec.FromKeywordText("annual"));
			viewModel.LoadFrom(DirectoryFilterSpec.Empty);
			viewModel.LoadFrom(new DirectoryFilterSpec
			{
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Text = "report" },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 1 },
				],
			});

			Assert.AreEqual(0, raised);
		}

		[TestMethod]
		public void BuildSpec_LengthOperatorWithoutValue_DropsHiddenText()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.NameCondition.Text = "report";
			viewModel.NameCondition.OperatorIndex = (int)DirectoryTextOperator.LengthGreaterThan;

			Assert.IsFalse(viewModel.NameCondition.IsActive);
			Assert.IsTrue(viewModel.BuildSpec().IsEmpty);

			viewModel.NameCondition.Length = 6;

			Assert.IsTrue(viewModel.NameCondition.IsActive);

			var column = viewModel.BuildSpec().Columns.Single();
			Assert.IsNull(column.Text);
			Assert.AreEqual(6, column.Length);
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

		[TestMethod]
		public void ShowNameCondition_AdvancedMode_HidesBlankEditorOnceConditionsExist()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};

			Assert.IsTrue(viewModel.ShowNameCondition);

			viewModel.AddCondition(DirectoryColumnKind.Type, DirectoryConditionJoin.And);
			Assert.IsFalse(viewModel.ShowNameCondition);

			viewModel.NameCondition.Text = "report";
			Assert.IsTrue(viewModel.ShowNameCondition);
		}

		[TestMethod]
		public void ShowNameCondition_BasicMode_IsHidden()
		{
			var viewModel = new DirectoryFilterBarViewModel();
			viewModel.AddCondition(DirectoryColumnKind.Type, DirectoryConditionJoin.And);

			Assert.IsFalse(viewModel.ShowNameCondition);
		}

		[TestMethod]
		public void ClearNameCondition_ResetsInlineNameEditor()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.NameCondition.Text = "report";

			viewModel.ClearNameCondition();

			Assert.IsFalse(viewModel.NameCondition.IsActive);
			Assert.IsTrue(viewModel.ShowNameCondition);
		}

		[TestMethod]
		public void UpdateConditionJoins_HidesConnectorBeforeFirstActiveCondition()
		{
			var viewModel = new DirectoryFilterBarViewModel
			{
				Mode = FilterMode.Advanced,
			};
			viewModel.AddCondition(DirectoryColumnKind.Name, DirectoryConditionJoin.And);
			viewModel.Conditions[0].Text = "report";
			viewModel.AddCondition(DirectoryColumnKind.Type, DirectoryConditionJoin.And);

			Assert.IsFalse(viewModel.Conditions[0].ShowsJoin);
			Assert.IsTrue(viewModel.Conditions[1].ShowsJoin);

			viewModel.NameCondition.Text = "draft";
			Assert.IsTrue(viewModel.Conditions[0].ShowsJoin);

			viewModel.ClearNameCondition();
			Assert.IsFalse(viewModel.Conditions[0].ShowsJoin);
		}

		[TestMethod]
		public void ApplyNow_RaisesCommitRequested()
		{
			var viewModel = new DirectoryFilterBarViewModel();
			var raised = 0;
			viewModel.CommitRequested += (_, _) => raised++;

			viewModel.ApplyNow();

			Assert.AreEqual(1, raised);
		}
	}
}
