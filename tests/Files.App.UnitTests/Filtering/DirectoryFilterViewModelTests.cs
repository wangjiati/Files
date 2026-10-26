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
	public sealed class DirectoryFilterViewModelTests
	{
		private static readonly DateTimeOffset MinDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		private static readonly DateTimeOffset MaxDate = new(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

		[TestMethod]
		public void Apply_NameContains_WritesNameBoxInsteadOfDuplicatingCondition()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty)
			{
				Text = "report",
				OperatorIndex = 0,
			};

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			Assert.AreEqual("report", result.NameContains);
			Assert.AreEqual(0, result.Columns.Count);
		}

		[TestMethod]
		public void Apply_NameStartsWith_WritesColumnConditionAndClearsNameBox()
		{
			var initial = new DirectoryFilterSpec { NameContains = "old" };
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial)
			{
				Text = "rep",
				OperatorIndex = 2,
			};

			var result = viewModel.ApplyTo(initial);

			Assert.IsNull(result.NameContains);
			Assert.AreEqual(1, result.Columns.Count);
			Assert.AreEqual(DirectoryColumnKind.Name, result.Columns[0].Column);
			Assert.AreEqual(DirectoryTextOperator.StartsWith, result.Columns[0].Operator);
			Assert.AreEqual("rep", result.Columns[0].Text);
		}

		[TestMethod]
		public void Apply_TypeChoices_ReplacesTypeAndExtensionButKeepsOtherColumns()
		{
			var initial = new DirectoryFilterSpec
			{
				NameContains = "keep",
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 10 },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["Old Type"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".old"] },
				],
			};
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Type, initial);
			viewModel.LoadChoices(
				[new DirectoryFilterChoice("Text Document", "Text Document")],
				[new DirectoryFilterChoice(".txt", ".txt")]);
			viewModel.Choices[0].IsSelected = true;

			var result = viewModel.ApplyTo(initial);

			Assert.AreEqual("keep", result.NameContains);
			Assert.AreEqual(1, result.Columns.Count(x => x.Column == DirectoryColumnKind.Size));
			Assert.IsFalse(result.Columns.Any(x => x.Column == DirectoryColumnKind.Extension));
			var typeCondition = result.Columns.Single(x => x.Column == DirectoryColumnKind.Type);
			CollectionAssert.AreEqual(new[] { "Text Document" }, typeCondition.Values.ToArray());
		}

		[TestMethod]
		public void Apply_ExtensionChoices_AddExtensionCondition()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Type, DirectoryFilterSpec.Empty);
			viewModel.LoadChoices(null, [new DirectoryFilterChoice(".txt", ".txt")]);
			viewModel.ExtensionChoices[0].IsSelected = true;

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			var extensionCondition = result.Columns.Single(x => x.Column == DirectoryColumnKind.Extension);
			CollectionAssert.AreEqual(new[] { ".txt" }, extensionCondition.Values.ToArray());
		}

		[TestMethod]
		public void Apply_SizeRange_ConvertsSelectedUnitToBytes()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Size, DirectoryFilterSpec.Empty)
			{
				SizeUnitIndex = 2,
				MinSizeText = "1.5",
				MaxSizeText = "2",
			};

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			var condition = result.Columns.Single();
			Assert.AreEqual((long)(1.5 * 1024 * 1024), condition.MinSize);
			Assert.AreEqual(2L * 1024 * 1024, condition.MaxSize);
		}

		[TestMethod]
		public void Apply_TypeIncludeEmpty_DoesNotCreateAnEmptyTypeCondition()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Type, DirectoryFilterSpec.Empty)
			{
				IncludeEmpty = true,
			};
			viewModel.LoadChoices(
				[new DirectoryFilterChoice("Text Document", "Text Document")],
				[new DirectoryFilterChoice(".txt", ".txt")]);

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			Assert.IsFalse(result.Columns.Any(x => x.Column == DirectoryColumnKind.Type));
			var extensionCondition = result.Columns.Single(x => x.Column == DirectoryColumnKind.Extension);
			Assert.IsTrue(extensionCondition.IncludeEmpty);
		}

		[TestMethod]
		public void Apply_ChoiceIncludeEmpty_SetsExplicitEmptySemantics()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Tags, DirectoryFilterSpec.Empty)
			{
				IncludeEmpty = true,
			};

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			Assert.IsTrue(result.Columns.Single().IncludeEmpty);
			Assert.AreEqual(0, result.Columns.Single().Values.Count);
		}

		[TestMethod]
		public void Apply_DateRange_UsesSelectedDates()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.ModifiedDate, DirectoryFilterSpec.Empty)
			{
				MinDate = MinDate,
				MaxDate = MaxDate,
			};

			var result = viewModel.ApplyTo(DirectoryFilterSpec.Empty);

			var condition = result.Columns.Single();
			Assert.AreEqual(DirectoryColumnKind.ModifiedDate, condition.Column);
			Assert.AreEqual(MinDate, condition.MinDate);
			Assert.AreEqual(MaxDate, condition.MaxDate);
		}

		[TestMethod]
		public void Clear_RemovesOnlyCurrentColumnConditions()
		{
			var initial = new DirectoryFilterSpec
			{
				NameContains = "report",
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Name, Operator = DirectoryTextOperator.EndsWith, Text = ".txt" },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["Text Document"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".txt"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 10 },
				],
			};

			var typeResult = new DirectoryFilterViewModel(DirectoryColumnKind.Type, initial).ClearFrom(initial);
			var nameResult = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial).ClearFrom(initial);

			Assert.AreEqual("report", typeResult.NameContains);
			Assert.IsFalse(typeResult.Columns.Any(x => x.Column is DirectoryColumnKind.Type or DirectoryColumnKind.Extension));
			Assert.IsTrue(typeResult.Columns.Any(x => x.Column == DirectoryColumnKind.Size));

			Assert.IsNull(nameResult.NameContains);
			Assert.IsFalse(nameResult.Columns.Any(x => x.Column == DirectoryColumnKind.Name));
			Assert.IsTrue(nameResult.Columns.Any(x => x.Column == DirectoryColumnKind.Size));
		}

		[TestMethod]
		public void Constructor_SeedsFromExistingSpec()
		{
			var initial = new DirectoryFilterSpec
			{
				NameContains = "report",
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 4096, MaxSize = 8192 },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Type, Values = ["Text Document"] },
				],
			};

			var nameViewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial);
			var sizeViewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Size, initial);
			var typeViewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Type, initial);
			typeViewModel.LoadChoices(
				[new DirectoryFilterChoice("Text Document", "Text Document"), new DirectoryFilterChoice("PNG Image", "PNG Image")]);

			Assert.AreEqual("report", nameViewModel.Text);
			Assert.AreEqual(0, nameViewModel.OperatorIndex);
			Assert.AreEqual("4", sizeViewModel.MinSizeText);
			Assert.AreEqual("8", sizeViewModel.MaxSizeText);
			Assert.AreEqual(1, sizeViewModel.SizeUnitIndex);
			Assert.IsTrue(typeViewModel.Choices.Single(x => x.Value == "Text Document").IsSelected);
			Assert.IsFalse(typeViewModel.Choices.Single(x => x.Value == "PNG Image").IsSelected);
		}

		[TestMethod]
		public void Apply_RegexMode_IgnoresTextAndClearsNameBox()
		{
			var initial = new DirectoryFilterSpec { NameContains = "old" };
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial)
			{
				UseRegex = true,
				Text = "abc",
				RegexPattern = @"\.md$",
				RegexTargetIndex = 0,
				RegexIgnoreCase = true,
			};

			var result = viewModel.ApplyTo(initial);

			Assert.IsNull(result.NameContains);
			Assert.IsFalse(result.Columns.Any(x => x.Column == DirectoryColumnKind.Name));
			Assert.IsNotNull(result.Regex);
			Assert.AreEqual(@"\.md$", result.Regex!.Pattern);
		}

		[TestMethod]
		public void Apply_TextMode_ClearsExistingRegex()
		{
			var initial = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"_old$", DirectoryRegexTarget.RawName, true),
			};
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial)
			{
				UseRegex = false,
				Text = "report",
			};

			var result = viewModel.ApplyTo(initial);

			Assert.IsNull(result.Regex);
			Assert.AreEqual("report", result.NameContains);
		}

		[TestMethod]
		public void Constructor_SeedsRegexMode()
		{
			var initial = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"\.md$", DirectoryRegexTarget.FullPath, IgnoreCase: false),
			};

			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial);

			Assert.IsTrue(viewModel.UseRegex);
			Assert.AreEqual(@"\.md$", viewModel.RegexPattern);
			Assert.AreEqual((int)DirectoryRegexTarget.FullPath, viewModel.RegexTargetIndex);
			Assert.IsFalse(viewModel.RegexIgnoreCase);
		}

		[TestMethod]
		public void IsColumnActive_ReportsSharedNameBoxAndGroupedTypeConditions()
		{
			var spec = new DirectoryFilterSpec
			{
				NameContains = "report",
				Columns =
				[
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Extension, Values = [".txt"] },
					new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = 10 },
				],
			};

			Assert.IsTrue(DirectoryFilterViewModel.IsColumnActive(spec, DirectoryColumnKind.Name));
			Assert.IsTrue(DirectoryFilterViewModel.IsColumnActive(spec, DirectoryColumnKind.Type));
			Assert.IsTrue(DirectoryFilterViewModel.IsColumnActive(spec, DirectoryColumnKind.Size));
			Assert.IsFalse(DirectoryFilterViewModel.IsColumnActive(spec, DirectoryColumnKind.ModifiedDate));
		}

		[TestMethod]
		public void ApplyDatePreset_SetsRangeRelativeToInjectedClock()
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key)
			{
				DatePresetIndex = 2,
			};

			viewModel.ApplyDatePreset();

			Assert.AreEqual(new DateTimeOffset(2026, 3, 9, 0, 0, 0, now.Offset), viewModel.MinDate);
			Assert.AreEqual(new DateTimeOffset(2026, 3, 16, 0, 0, 0, now.Offset).AddTicks(-1), viewModel.MaxDate);
		}
	}
}
