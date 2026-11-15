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

		[TestMethod]
		public void ToColumnFilter_IncludesJoinScopeLengthAndDatePreset()
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.ModifiedDate, DirectoryFilterSpec.Empty, () => now, key => key)
			{
				Join = DirectoryConditionJoin.Or,
				Scope = DirectoryNameScope.RawName,
				Length = 12,
			};

			viewModel.ApplyDatePreset(DirectoryDatePreset.Last6Hours);
			var condition = viewModel.ToColumnFilter();

			Assert.AreEqual(DirectoryColumnKind.ModifiedDate, condition.Column);
			Assert.AreEqual(DirectoryConditionJoin.Or, condition.Join);
			Assert.AreEqual(DirectoryNameScope.RawName, condition.Scope);
			Assert.AreEqual(12, condition.Length);
			Assert.AreEqual(now.AddHours(-6), condition.MinDate);
			Assert.AreEqual(now, condition.MaxDate);
		}

		[TestMethod]
		public void LoadFrom_RestoresJoinScopeLengthAndText()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			var condition = new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Name,
				Join = DirectoryConditionJoin.Or,
				Operator = DirectoryTextOperator.EndsWith,
				Text = ".txt",
				Length = 9,
				Scope = DirectoryNameScope.RawName,
			};

			viewModel.LoadFrom(condition);

			Assert.AreEqual(DirectoryConditionJoin.Or, viewModel.Join);
			Assert.AreEqual((int)DirectoryTextOperator.EndsWith, viewModel.OperatorIndex);
			Assert.AreEqual(".txt", viewModel.Text);
			Assert.AreEqual(9, viewModel.Length);
			Assert.AreEqual(DirectoryNameScope.RawName, viewModel.Scope);
		}

		[TestMethod]
		public void ScopeIndex_IgnoresInvalidValuesAndRoundTrips()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);

			// A combo box reports -1 when its item source is reset; that must not throw or move the scope.
			viewModel.ScopeIndex = -1;
			Assert.AreEqual(DirectoryNameScope.DisplayName, viewModel.Scope);
			Assert.AreEqual(0, viewModel.ScopeIndex);

			viewModel.ScopeIndex = (int)DirectoryNameScope.RawName;
			Assert.AreEqual(DirectoryNameScope.RawName, viewModel.Scope);
			Assert.AreEqual((int)DirectoryNameScope.RawName, viewModel.ScopeIndex);

			viewModel.Scope = DirectoryNameScope.DisplayName;
			Assert.AreEqual(0, viewModel.ScopeIndex);

			viewModel.ScopeIndex = 99;
			Assert.AreEqual(DirectoryNameScope.DisplayName, viewModel.Scope);
		}

		[TestMethod]
		public void LoadFrom_ChoiceCondition_SelectsAlreadyLoadedChoices()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Tags, DirectoryFilterSpec.Empty);
			viewModel.LoadChoices(
				[new DirectoryFilterChoice("work", "Work"), new DirectoryFilterChoice("home", "Home")]);

			viewModel.LoadFrom(new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Tags,
				Values = ["work"],
				IncludeEmpty = true,
			});

			Assert.IsTrue(viewModel.Choices.Single(x => x.Value == "work").IsSelected);
			Assert.IsFalse(viewModel.Choices.Single(x => x.Value == "home").IsSelected);
			Assert.IsTrue(viewModel.IncludeEmpty);
		}

		[TestMethod]
		public void ToColumnFilter_IncludesRegex()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty)
			{
				UseRegex = true,
				RegexPattern = @"\.md$",
				RegexTargetIndex = (int)DirectoryRegexTarget.RawName,
				RegexIgnoreCase = false,
				Text = "ignored",
			};

			var condition = viewModel.ToColumnFilter();

			Assert.IsTrue(viewModel.IsActive);
			Assert.IsNull(condition.Text);
			Assert.IsNotNull(condition.Regex);
			Assert.AreEqual(@"\.md$", condition.Regex!.Pattern);
			Assert.AreEqual(DirectoryRegexTarget.RawName, condition.Regex.Target);
			Assert.IsFalse(condition.Regex.IgnoreCase);
		}

		[TestMethod]
		public void LoadFrom_RestoresRegexAndRoundTrips()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			var condition = new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Name,
				Regex = new DirectoryRegexOptions(@"^C:\\Items\\.*\.txt$", DirectoryRegexTarget.FullPath, IgnoreCase: false),
			};

			viewModel.LoadFrom(condition);

			Assert.IsTrue(viewModel.UseRegex);
			Assert.AreEqual(@"^C:\\Items\\.*\.txt$", viewModel.RegexPattern);
			Assert.AreEqual((int)DirectoryRegexTarget.FullPath, viewModel.RegexTargetIndex);
			Assert.IsFalse(viewModel.RegexIgnoreCase);
			Assert.IsTrue(viewModel.IsActive);
			Assert.AreEqual(condition.Regex, viewModel.ToColumnFilter().Regex);

			viewModel.LoadFrom(new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Name,
				Text = "report",
			});

			Assert.IsFalse(viewModel.UseRegex);
			Assert.IsNull(viewModel.ToColumnFilter().Regex);
		}

		[TestMethod]
		public void IsActive_UsesColumnValuesAndIgnoresBlankDrafts()
		{
			var name = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			Assert.IsFalse(name.IsActive);
			name.Text = " ";
			Assert.IsFalse(name.IsActive);
			name.Text = "report";
			Assert.IsTrue(name.IsActive);
			name.Text = null;
			name.OperatorIndex = (int)DirectoryTextOperator.LengthEquals;
			name.Length = 6;
			Assert.IsTrue(name.IsActive);

			var type = new DirectoryFilterViewModel(DirectoryColumnKind.Type, DirectoryFilterSpec.Empty);
			type.LoadChoices([new DirectoryFilterChoice("Text Document", "Text Document")]);
			Assert.IsFalse(type.IsActive);
			type.Choices[0].IsSelected = true;
			Assert.IsTrue(type.IsActive);

			var size = new DirectoryFilterViewModel(DirectoryColumnKind.Size, DirectoryFilterSpec.Empty);
			Assert.IsFalse(size.IsActive);
			size.MinSizeText = "1";
			Assert.IsTrue(size.IsActive);

			var date = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty);
			Assert.IsFalse(date.IsActive);
			date.MaxDate = MaxDate;
			Assert.IsTrue(date.IsActive);
		}

		[TestMethod]
		[DataRow(DirectoryDatePreset.LastHour, -1)]
		[DataRow(DirectoryDatePreset.Last6Hours, -6)]
		[DataRow(DirectoryDatePreset.Last24Hours, -24)]
		public void ApplyDatePreset_ComputesRelativeRange(DirectoryDatePreset preset, int hourOffset)
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key);

			viewModel.ApplyDatePreset(preset);

			Assert.AreEqual(now.AddHours(hourOffset), viewModel.MinDate);
			Assert.AreEqual(now, viewModel.MaxDate);
		}

		[TestMethod]
		[DataRow(DirectoryDatePreset.Last3Days, -3)]
		[DataRow(DirectoryDatePreset.Last7Days, -7)]
		[DataRow(DirectoryDatePreset.Last30Days, -30)]
		public void ApplyDatePreset_DayPresets_CountBackFromClock(DirectoryDatePreset preset, int dayOffset)
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key);

			viewModel.ApplyDatePreset(preset);

			Assert.AreEqual(now.AddDays(dayOffset), viewModel.MinDate);
			Assert.AreEqual(now, viewModel.MaxDate);
		}

		[TestMethod]
		public void ApplyDatePreset_Yesterday_SpansWholePreviousDay()
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key);

			viewModel.ApplyDatePreset(DirectoryDatePreset.Yesterday);

			var yesterday = new DateTimeOffset(2026, 3, 14, 0, 0, 0, now.Offset);
			Assert.AreEqual(yesterday, viewModel.MinDate);
			Assert.AreEqual(yesterday.AddDays(1).AddTicks(-1), viewModel.MaxDate);
		}

		[TestMethod]
		public void ApplyDatePreset_ThisMonth_StartsAtFirstDayMidnight()
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key);

			viewModel.ApplyDatePreset(DirectoryDatePreset.ThisMonth);

			Assert.AreEqual(new DateTimeOffset(2026, 3, 1, 0, 0, 0, now.Offset), viewModel.MinDate);
			Assert.AreEqual(now, viewModel.MaxDate);
		}

		[TestMethod]
		public void ApplyDatePreset_NoneAndCustom_LeaveBoundsUnchanged()
		{
			var now = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(8));
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.CreatedDate, DirectoryFilterSpec.Empty, () => now, key => key)
			{
				MinDate = MinDate,
				MaxDate = MaxDate,
			};

			viewModel.ApplyDatePreset(DirectoryDatePreset.None);
			Assert.AreEqual(MinDate, viewModel.MinDate);
			Assert.AreEqual(MaxDate, viewModel.MaxDate);

			viewModel.ApplyDatePreset(DirectoryDatePreset.Custom);
			Assert.AreEqual(MinDate, viewModel.MinDate);
			Assert.AreEqual(MaxDate, viewModel.MaxDate);
		}
	}
}
