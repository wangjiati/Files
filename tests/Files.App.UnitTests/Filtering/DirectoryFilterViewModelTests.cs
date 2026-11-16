// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
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
		public void ToColumnFilter_LengthOperator_DropsHiddenText()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty)
			{
				Text = "report",
				OperatorIndex = (int)DirectoryTextOperator.LengthGreaterThan,
				Length = 6,
			};

			var condition = viewModel.ToColumnFilter();

			Assert.IsNull(condition.Text);
			Assert.AreEqual(6, condition.Length);
		}

		[TestMethod]
		public void ToColumnFilter_MapsCaseSensitive()
		{
			var defaultViewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			Assert.IsFalse(defaultViewModel.CaseSensitive);
			Assert.IsFalse(defaultViewModel.ToColumnFilter().CaseSensitive);

			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty)
			{
				Text = "report",
				CaseSensitive = true,
			};

			Assert.IsTrue(viewModel.ToColumnFilter().CaseSensitive);
		}

		[TestMethod]
		public void LoadFrom_RestoresCaseSensitive()
		{
			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);

			viewModel.LoadFrom(new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Name,
				Text = "report",
				CaseSensitive = true,
			});

			Assert.IsTrue(viewModel.CaseSensitive);
			Assert.IsTrue(viewModel.ToColumnFilter().CaseSensitive);
		}

		[TestMethod]
		public void RegexTargetChoices_OfferOnlyNameTargets()
		{
			var viewModel = new DirectoryFilterViewModel(
				DirectoryColumnKind.Name,
				DirectoryFilterSpec.Empty,
				localize: key => key);

			Assert.AreEqual(2, viewModel.RegexTargetChoices.Length);
			CollectionAssert.DoesNotContain(viewModel.RegexTargetChoices, Strings.FilterTargetFullPath);
			Assert.AreEqual(0, viewModel.RegexTargetIndex);

			viewModel.RegexTargetIndex = 1;
			Assert.AreEqual(DirectoryRegexTarget.DisplayName, viewModel.RegexTarget);

			// A stale index from the removed target must be ignored.
			viewModel.RegexTargetIndex = 2;
			Assert.AreEqual(DirectoryRegexTarget.DisplayName, viewModel.RegexTarget);
		}

		[TestMethod]
		public void Constructor_SeedsRegexMode()
		{
			var initial = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"\.md$", DirectoryRegexTarget.DisplayName, IgnoreCase: false),
			};

			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial);

			Assert.IsTrue(viewModel.UseRegex);
			Assert.AreEqual(@"\.md$", viewModel.RegexPattern);
			Assert.AreEqual((int)DirectoryRegexTarget.DisplayName, viewModel.RegexTargetIndex);
			Assert.IsFalse(viewModel.RegexIgnoreCase);
		}

		[TestMethod]
		public void Constructor_MapsLegacyFullPathRegexTargetToRawName()
		{
			var initial = new DirectoryFilterSpec
			{
				Regex = new DirectoryRegexOptions(@"\.md$", DirectoryRegexTarget.FullPath, IgnoreCase: true),
			};

			var viewModel = new DirectoryFilterViewModel(DirectoryColumnKind.Name, initial);

			Assert.AreEqual(0, viewModel.RegexTargetIndex);
			Assert.AreEqual(DirectoryRegexTarget.RawName, viewModel.RegexTarget);
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

			Assert.AreEqual(now.AddHours(-6), viewModel.MinDate);
			Assert.AreEqual(now, viewModel.MaxDate);

			viewModel.DatePresetIndex = 7;
			viewModel.ApplyDatePreset();

			Assert.AreEqual(now.AddDays(-7), viewModel.MinDate);
			Assert.AreEqual(now, viewModel.MaxDate);
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
				Regex = new DirectoryRegexOptions(@"^report$", DirectoryRegexTarget.DisplayName, IgnoreCase: false),
			};

			viewModel.LoadFrom(condition);

			Assert.IsTrue(viewModel.UseRegex);
			Assert.AreEqual(@"^report$", viewModel.RegexPattern);
			Assert.AreEqual((int)DirectoryRegexTarget.DisplayName, viewModel.RegexTargetIndex);
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
