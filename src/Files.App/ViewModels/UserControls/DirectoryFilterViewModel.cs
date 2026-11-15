// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Files.App.ViewModels.UserControls
{
	/// <summary>
	/// Selectable value shown in the column filter flyout.
	/// </summary>
	public sealed partial class DirectoryFilterValueOption : ObservableObject
	{
		[ObservableProperty]
		private bool isSelected;

		public DirectoryFilterValueOption(string value, string label)
		{
			Value = value;
			Label = label;
		}

		public string Value { get; }

		public string Label { get; }

		public override string ToString() => Label;
	}

	/// <summary>
	/// Date range preset for date columns. Relative presets are recomputed from the current clock.
	/// </summary>
	public enum DirectoryDatePreset
	{
		None,
		LastHour,
		Last6Hours,
		Last24Hours,
		Today,
		Yesterday,
		Last3Days,
		Last7Days,
		Last30Days,
		ThisMonth,
		ThisYear,
		Custom,
	}

	/// <summary>
	/// Edits one column condition of the shared <see cref="DirectoryFilterSpec"/> without touching other columns.
	/// </summary>
	public sealed partial class DirectoryFilterViewModel : ObservableObject
	{
		private static readonly long[] SizeUnitMultipliers = [1, 1024, 1024L * 1024, 1024L * 1024 * 1024];

		/// <summary>
		/// Regex name fields offered by the editor. Full path remains only for decoding legacy addresses.
		/// </summary>
		private static readonly DirectoryRegexTarget[] RegexTargets =
		[
			DirectoryRegexTarget.RawName,
			DirectoryRegexTarget.DisplayName,
		];

		private readonly DirectoryColumnKind column;
		private readonly Func<DateTimeOffset> clock;
		private readonly Func<string, string> localize;
		private readonly List<string> seededValues = [];
		private readonly List<string> seededExtensions = [];

		[ObservableProperty]
		private DirectoryConditionJoin join;

		[ObservableProperty]
		private DirectoryNameScope scope;

		[ObservableProperty]
		private int? length;

		[ObservableProperty]
		private string? text;

		[ObservableProperty]
		private bool caseSensitive;

		[ObservableProperty]
		private int operatorIndex;

		[ObservableProperty]
		private bool includeEmpty;

		[ObservableProperty]
		private string? minSizeText;

		[ObservableProperty]
		private string? maxSizeText;

		[ObservableProperty]
		private int sizeUnitIndex;

		[ObservableProperty]
		private DateTimeOffset? minDate;

		[ObservableProperty]
		private DateTimeOffset? maxDate;

		[ObservableProperty]
		private int datePresetIndex;

		[ObservableProperty]
		private bool useRegex;

		[ObservableProperty]
		private string? regexPattern;

		[ObservableProperty]
		private bool regexIgnoreCase = true;

		private DirectoryRegexTarget regexTarget = DirectoryRegexTarget.RawName;

		/// <summary>
		/// Regex name field to match against.
		/// </summary>
		public DirectoryRegexTarget RegexTarget
		{
			get => regexTarget;
			set
			{
				// Addresses written before the full path target was removed keep working by matching
				// the full file name instead.
				var normalized = value is DirectoryRegexTarget.FullPath
					? DirectoryRegexTarget.RawName
					: value;

				if (SetProperty(ref regexTarget, normalized))
					OnPropertyChanged(nameof(RegexTargetIndex));
			}
		}

		/// <summary>
		/// <see cref="RegexTarget"/> as a combo box index. Indexes outside the offered targets are ignored.
		/// </summary>
		public int RegexTargetIndex
		{
			get => Math.Max(0, Array.IndexOf(RegexTargets, RegexTarget));
			set
			{
				if (value is < 0 || value >= RegexTargets.Length)
					return;

				RegexTarget = RegexTargets[value];
			}
		}

		public DirectoryFilterViewModel(DirectoryColumnKind column, DirectoryFilterSpec currentSpec, Func<DateTimeOffset>? clock = null, Func<string, string>? localize = null)
		{
			this.column = column;
			this.clock = clock ?? (() => DateTimeOffset.Now);
			this.localize = localize ?? (resourceKey => resourceKey.GetLocalizedResource());

			Seed(currentSpec);
		}

		public DirectoryColumnKind Column => column;

		public string Title => localize(column switch
		{
			DirectoryColumnKind.Type => Strings.Type,
			DirectoryColumnKind.Extension => Strings.FilterExtensionSection,
			DirectoryColumnKind.Tags => Strings.Tag,
			DirectoryColumnKind.Size => Strings.Size,
			DirectoryColumnKind.ModifiedDate => Strings.DateModifiedLowerCase,
			DirectoryColumnKind.CreatedDate => Strings.DateCreated,
			_ => Strings.Name,
		});

		public bool IsTextColumn => column is DirectoryColumnKind.Name;

		public bool IsNameColumn => column is DirectoryColumnKind.Name;

		public bool IsChoiceColumn => column is DirectoryColumnKind.Type or DirectoryColumnKind.Extension or DirectoryColumnKind.Tags;

		public bool ShowsExtensionChoices => column is DirectoryColumnKind.Type or DirectoryColumnKind.Extension;

		public bool IsSizeColumn => column is DirectoryColumnKind.Size;

		public bool IsDateColumn => column is DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate;

		public string[] OperatorChoices =>
		[
			localize(Strings.FilterOperatorContains),
			localize(Strings.FilterOperatorNotContains),
			localize(Strings.FilterOperatorStartsWith),
			localize(Strings.FilterOperatorEndsWith),
			localize(Strings.FilterOperatorEquals),
			localize(Strings.FilterOperatorStartsWithNot),
			localize(Strings.FilterOperatorEndsWithNot),
			localize(Strings.FilterOperatorNotEquals),
			localize(Strings.FilterOperatorLengthGreaterThan),
			localize(Strings.FilterOperatorLengthLessThan),
			localize(Strings.FilterOperatorLengthEquals),
			localize(Strings.FilterOperatorLengthNotEquals),
		];

		public string[] ScopeChoices =>
		[
			localize(Strings.FilterScopeDisplayName),
			localize(Strings.FilterScopeRawName),
		];

		/// <summary>
		/// <see cref="Scope"/> as a combo box index. The transient -1 a reset selection reports is ignored.
		/// </summary>
		public int ScopeIndex
		{
			get => (int)Scope;
			set
			{
				if (value is < 0 or > (int)DirectoryNameScope.RawName)
					return;

				Scope = (DirectoryNameScope)value;
			}
		}

		public string[] SizeUnitChoices { get; } = ["B", "KB", "MB", "GB"];

		public string[] DatePresetChoices =>
		[
			localize(Strings.FilterAllDates),
			localize(Strings.FilterDatePresetLastHour),
			localize(Strings.FilterDatePresetLast6Hours),
			localize(Strings.FilterDatePresetLast24Hours),
			localize(Strings.FilterDatePresetToday),
			localize(Strings.FilterDatePresetYesterday),
			localize(Strings.FilterDatePresetLast3Days),
			localize(Strings.FilterDatePresetLast7Days),
			localize(Strings.FilterDatePresetLast30Days),
			localize(Strings.FilterDatePresetThisMonth),
			localize(Strings.FilterDatePresetThisYear),
			localize(Strings.FilterDatePresetCustom),
		];

		public string[] RegexTargetChoices =>
		[
			localize(Strings.FilterTargetRawName),
			localize(Strings.FilterTargetDisplayName),
		];

		/// <summary>
		/// Validation message for the current regex; null when it can be applied.
		/// </summary>
		public string? RegexError { get; private set; }

		/// <summary>
		/// Whether the selected operator compares the name length instead of its text.
		/// </summary>
		public bool IsLengthOperator
			=> (DirectoryTextOperator)Math.Clamp(OperatorIndex, 0, (int)DirectoryTextOperator.Wildcard) is
				DirectoryTextOperator.LengthGreaterThan or
				DirectoryTextOperator.LengthLessThan or
				DirectoryTextOperator.LengthEquals or
				DirectoryTextOperator.LengthNotEquals;

		/// <summary>
		/// Whether this editor currently contributes a meaningful condition to the filter chain.
		/// Blank drafts stay inactive so they never establish an OR group that matches everything.
		/// </summary>
		public bool IsActive
			=> column switch
			{
				DirectoryColumnKind.Name => UseRegex
					? !string.IsNullOrWhiteSpace(RegexPattern)
					: IsLengthOperator
						? HasLengthValue
						: !string.IsNullOrWhiteSpace(Text),
				DirectoryColumnKind.Type => Choices.Any(option => option.IsSelected),
				DirectoryColumnKind.Extension => ExtensionChoices.Any(option => option.IsSelected) || IncludeEmpty,
				DirectoryColumnKind.Tags => Choices.Any(option => option.IsSelected) || IncludeEmpty,
				DirectoryColumnKind.Size => ToBytes(MinSizeText) is not null || ToBytes(MaxSizeText) is not null,
				DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate => MinDate is not null || MaxDate is not null,
				_ => false,
			};

		private ObservableCollection<DirectoryFilterValueOption> choices = [];
		private ObservableCollection<DirectoryFilterValueOption> extensionChoices = [];

		public ObservableCollection<DirectoryFilterValueOption> Choices
		{
			get => choices;
			private set
			{
				if (ReferenceEquals(choices, value))
					return;

				foreach (var option in choices)
					option.PropertyChanged -= OnChoiceOptionChanged;

				choices = value;

				foreach (var option in value)
					option.PropertyChanged += OnChoiceOptionChanged;

				OnPropertyChanged();
				OnPropertyChanged(nameof(IsActive));
			}
		}

		public ObservableCollection<DirectoryFilterValueOption> ExtensionChoices
		{
			get => extensionChoices;
			private set
			{
				if (ReferenceEquals(extensionChoices, value))
					return;

				foreach (var option in extensionChoices)
					option.PropertyChanged -= OnChoiceOptionChanged;

				extensionChoices = value;

				foreach (var option in value)
					option.PropertyChanged += OnChoiceOptionChanged;

				OnPropertyChanged();
				OnPropertyChanged(nameof(IsActive));
			}
		}

		partial void OnTextChanged(string? value) => NotifyIsActiveChanged();

		partial void OnScopeChanged(DirectoryNameScope value)
		{
			OnPropertyChanged(nameof(ScopeIndex));
			OnPropertyChanged(nameof(UseRawName));
		}

		/// <summary>
		/// Compact scope switch used by the inline editor: raw file name vs display name.
		/// </summary>
		public bool UseRawName
		{
			get => Scope is DirectoryNameScope.RawName;
			set => Scope = value ? DirectoryNameScope.RawName : DirectoryNameScope.DisplayName;
		}

		/// <summary>
		/// True when the date condition uses a custom range and the inline editor shows the pickers.
		/// </summary>
		public bool IsCustomDatePreset => DatePresetIndex == (int)DirectoryDatePreset.Custom;

		partial void OnDatePresetIndexChanged(int value) => OnPropertyChanged(nameof(IsCustomDatePreset));

		partial void OnOperatorIndexChanged(int value)
		{
			OnPropertyChanged(nameof(IsLengthOperator));
			NotifyIsActiveChanged();
		}

		partial void OnLengthChanged(int? value) => NotifyIsActiveChanged();

		partial void OnUseRegexChanged(bool value) => NotifyIsActiveChanged();

		partial void OnRegexPatternChanged(string? value) => NotifyIsActiveChanged();

		partial void OnIncludeEmptyChanged(bool value) => NotifyIsActiveChanged();

		partial void OnMinSizeTextChanged(string? value) => NotifyIsActiveChanged();

		partial void OnMaxSizeTextChanged(string? value) => NotifyIsActiveChanged();

		partial void OnMinDateChanged(DateTimeOffset? value) => NotifyIsActiveChanged();

		partial void OnMaxDateChanged(DateTimeOffset? value) => NotifyIsActiveChanged();

		private void NotifyIsActiveChanged() => OnPropertyChanged(nameof(IsActive));

		private void OnChoiceOptionChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(DirectoryFilterValueOption.IsSelected))
				NotifyIsActiveChanged();
		}

		public static bool IsColumnActive(DirectoryFilterSpec spec, DirectoryColumnKind column)
			=> column switch
			{
				DirectoryColumnKind.Name => !string.IsNullOrEmpty(spec.NameContains) || spec.HasColumnFilter(DirectoryColumnKind.Name) || spec.Regex is not null,
				DirectoryColumnKind.Type => spec.HasColumnFilter(DirectoryColumnKind.Type) || spec.HasColumnFilter(DirectoryColumnKind.Extension),
				_ => spec.HasColumnFilter(column),
			};

		public void LoadChoices(IReadOnlyList<DirectoryFilterChoice>? choices = null, IReadOnlyList<DirectoryFilterChoice>? extensions = null)
		{
			Choices = BuildOptions(choices, seededValues);
			ExtensionChoices = BuildOptions(extensions, seededExtensions);
		}

		public void ApplyDatePreset()
		{
			var now = clock();
			var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
			var endOfToday = today.AddDays(1).AddTicks(-1);

			(MinDate, MaxDate) = DatePresetIndex switch
			{
				1 => (today, endOfToday),
				2 => (today.AddDays(-6), endOfToday),
				3 => (today.AddDays(-29), endOfToday),
				4 => (new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset), endOfToday),
				_ => ((DateTimeOffset?)null, (DateTimeOffset?)null),
			};
		}

		/// <summary>
		/// Applies a relative preset from the current clock. None and Custom leave the bounds to the caller.
		/// </summary>
		public void ApplyDatePreset(DirectoryDatePreset preset)
		{
			if (preset is DirectoryDatePreset.None or DirectoryDatePreset.Custom)
				return;

			var now = clock();
			var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);

			(MinDate, MaxDate) = preset switch
			{
				DirectoryDatePreset.LastHour => (now.AddHours(-1), now),
				DirectoryDatePreset.Last6Hours => (now.AddHours(-6), now),
				DirectoryDatePreset.Last24Hours => (now.AddHours(-24), now),
				DirectoryDatePreset.Today => (today, now),
				DirectoryDatePreset.Yesterday => (today.AddDays(-1), today.AddTicks(-1)),
				DirectoryDatePreset.Last3Days => (now.AddDays(-3), now),
				DirectoryDatePreset.Last7Days => (now.AddDays(-7), now),
				DirectoryDatePreset.Last30Days => (now.AddDays(-30), now),
				DirectoryDatePreset.ThisMonth => (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset), now),
				DirectoryDatePreset.ThisYear => (new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset), now),
				_ => (MinDate, MaxDate),
			};
		}

		/// <summary>
		/// Materializes the editor as one condition in the filter bar's condition chain.
		/// The regex mode lives on the spec, not on a column condition.
		/// </summary>
		public DirectoryColumnFilter ToColumnFilter()
		{
			var useRegex = column is DirectoryColumnKind.Name && UseRegex && !string.IsNullOrWhiteSpace(RegexPattern);

			List<string> values = column switch
			{
				DirectoryColumnKind.Extension => GetSelectedValues(ExtensionChoices),
				DirectoryColumnKind.Type or DirectoryColumnKind.Tags => GetSelectedValues(Choices),
				_ => [],
			};

			return new DirectoryColumnFilter
			{
				Join = Join,
				Column = column,
				Operator = (DirectoryTextOperator)Math.Clamp(OperatorIndex, 0, (int)DirectoryTextOperator.Wildcard),
				// Length operators compare the name length, so a leftover draft text must not leak into the condition.
				Text = useRegex || IsLengthOperator ? null : Text?.Trim(),
				Length = useRegex ? null : Length,
				Scope = Scope,
				CaseSensitive = column is DirectoryColumnKind.Name && CaseSensitive,
				Regex = useRegex
					? new DirectoryRegexOptions(
						RegexPattern!.Trim(),
						RegexTarget,
						RegexIgnoreCase)
					: null,
				Values = values,
				IncludeEmpty = column is DirectoryColumnKind.Extension or DirectoryColumnKind.Tags && IncludeEmpty,
				MinSize = column is DirectoryColumnKind.Size ? ToBytes(MinSizeText) : null,
				MaxSize = column is DirectoryColumnKind.Size ? ToBytes(MaxSizeText) : null,
				MinDate = column is DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate ? MinDate : null,
				MaxDate = column is DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate ? MaxDate : null,
			};
		}

		/// <summary>
		/// Restores one condition, replacing the values edited earlier.
		/// </summary>
		public void LoadFrom(DirectoryColumnFilter filter)
		{
			Join = filter.Join;
			Scope = filter.Scope;
			CaseSensitive = filter.CaseSensitive;
			Length = filter.Length;

			switch (filter.Column)
			{
				case DirectoryColumnKind.Name:
					Text = filter.Text;
					OperatorIndex = (int)filter.Operator;

					if (filter.Regex is { } regex)
					{
						UseRegex = true;
						RegexPattern = regex.Pattern;
						RegexTarget = regex.Target;
						RegexIgnoreCase = regex.IgnoreCase;

						if (regex.Target is DirectoryRegexTarget.RawName or DirectoryRegexTarget.DisplayName)
						{
							Scope = regex.Target is DirectoryRegexTarget.RawName
								? DirectoryNameScope.RawName
								: DirectoryNameScope.DisplayName;
						}
					}
					else
					{
						UseRegex = false;
						RegexPattern = null;
					}
					break;

				case DirectoryColumnKind.Type:
					seededValues.Clear();
					seededValues.AddRange(filter.Values);
					IncludeEmpty = filter.IncludeEmpty;
					ApplySelection(Choices, filter.Values);
					break;

				case DirectoryColumnKind.Extension:
					seededExtensions.Clear();
					seededExtensions.AddRange(filter.Values);
					IncludeEmpty = filter.IncludeEmpty;
					ApplySelection(ExtensionChoices, filter.Values);
					break;

				case DirectoryColumnKind.Tags:
					seededValues.Clear();
					seededValues.AddRange(filter.Values);
					IncludeEmpty = filter.IncludeEmpty;
					ApplySelection(Choices, filter.Values);
					break;

				case DirectoryColumnKind.Size:
					SeedSize(filter.MinSize, filter.MaxSize);
					break;

				case DirectoryColumnKind.ModifiedDate:
				case DirectoryColumnKind.CreatedDate:
					MinDate = filter.MinDate;
					MaxDate = filter.MaxDate;
					break;
			}
		}

		/// <summary>
		/// Validates the planned regex without touching the shared spec. The flyout must call this before applying.
		/// </summary>
		public bool ValidateRegex()
		{
			RegexError = null;

			if (!UseRegex)
				return true;

			if (string.IsNullOrWhiteSpace(RegexPattern))
			{
				RegexError = localize(Strings.FilterRegexEmpty);
				return false;
			}

			if (!DirectoryRegexFilter.TryCreate(BuildRegexOptions(), out _, out var error))
			{
				RegexError = localize(error is DirectoryRegexError.PatternTooLong
					? Strings.FilterRegexTooLong
					: Strings.FilterRegexInvalid);
				return false;
			}

			return true;
		}

		private DirectoryRegexOptions BuildRegexOptions()
			=> new(
				RegexPattern?.Trim() ?? string.Empty,
				RegexTarget,
				RegexIgnoreCase);

		private static List<string> GetSelectedValues(IReadOnlyList<DirectoryFilterValueOption> options)
			=> options.Where(option => option.IsSelected).Select(option => option.Value).ToList();

		private bool HasLengthValue
			=> Length is not null
				&& (DirectoryTextOperator)OperatorIndex is DirectoryTextOperator.LengthGreaterThan
					or DirectoryTextOperator.LengthLessThan
					or DirectoryTextOperator.LengthEquals
					or DirectoryTextOperator.LengthNotEquals;

		private static void ApplySelection(IReadOnlyList<DirectoryFilterValueOption> options, IReadOnlyList<string> values)
		{
			foreach (var option in options)
				option.IsSelected = values.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
		}

		private long? ToBytes(string? text)
		{
			if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var amount))
				return null;

			var multiplier = SizeUnitMultipliers[Math.Clamp(SizeUnitIndex, 0, SizeUnitMultipliers.Length - 1)];
			return (long)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
		}

		private void Seed(DirectoryFilterSpec spec)
		{
			switch (column)
			{
				case DirectoryColumnKind.Name:
					if (spec.Columns.FirstOrDefault(filter => filter.Column == DirectoryColumnKind.Name) is { } nameFilter)
					{
						Text = nameFilter.Text;
						OperatorIndex = (int)nameFilter.Operator;
						CaseSensitive = nameFilter.CaseSensitive;
					}
					else if (!string.IsNullOrEmpty(spec.NameContains))
					{
						Text = spec.NameContains;
						OperatorIndex = (int)DirectoryTextOperator.Contains;
					}

					if (spec.Regex is { } regex)
					{
						UseRegex = true;
						RegexPattern = regex.Pattern;
						RegexTarget = regex.Target;
						RegexIgnoreCase = regex.IgnoreCase;
					}
					break;

				case DirectoryColumnKind.Type:
					SeedValues(spec, DirectoryColumnKind.Type, seededValues);
					SeedValues(spec, DirectoryColumnKind.Extension, seededExtensions);
					break;

				case DirectoryColumnKind.Tags:
					SeedValues(spec, DirectoryColumnKind.Tags, seededValues);
					break;

				case DirectoryColumnKind.Size:
					if (spec.Columns.FirstOrDefault(filter => filter.Column == DirectoryColumnKind.Size) is { } sizeFilter)
						SeedSize(sizeFilter.MinSize, sizeFilter.MaxSize);
					break;

				case DirectoryColumnKind.ModifiedDate:
				case DirectoryColumnKind.CreatedDate:
					if (spec.Columns.FirstOrDefault(filter => filter.Column == column) is { } dateFilter)
					{
						MinDate = dateFilter.MinDate;
						MaxDate = dateFilter.MaxDate;
					}
					break;
			}
		}

		private void SeedSize(long? min, long? max)
		{
			// Prefer the largest unit that represents both bounds exactly, so "1.5 MB" reopens as 1.5 MB.
			var unitIndex = 0;
			for (var index = SizeUnitMultipliers.Length - 1; index > 0; index--)
			{
				var multiplier = SizeUnitMultipliers[index];

				if ((min is null || min % multiplier == 0) && (max is null || max % multiplier == 0))
				{
					unitIndex = index;
					break;
				}
			}

			SizeUnitIndex = unitIndex;
			var divisor = SizeUnitMultipliers[unitIndex];

			MinSizeText = min is { } minValue
				? ((double)minValue / divisor).ToString(System.Globalization.CultureInfo.CurrentCulture)
				: null;

			MaxSizeText = max is { } maxValue
				? ((double)maxValue / divisor).ToString(System.Globalization.CultureInfo.CurrentCulture)
				: null;
		}

		private void SeedValues(DirectoryFilterSpec spec, DirectoryColumnKind column, List<string> target)
		{
			var condition = spec.Columns.FirstOrDefault(filter => filter.Column == column);

			if (condition is null)
				return;

			target.AddRange(condition.Values);

			if (column == this.column)
				IncludeEmpty = condition.IncludeEmpty;
		}

		private static ObservableCollection<DirectoryFilterValueOption> BuildOptions(IReadOnlyList<DirectoryFilterChoice>? choices, List<string> selectedValues)
		{
			if (choices is null)
				return [];

			return new ObservableCollection<DirectoryFilterValueOption>(
				choices.Select(choice => new DirectoryFilterValueOption(choice.Value, choice.Label)
				{
					IsSelected = selectedValues.Contains(choice.Value, StringComparer.OrdinalIgnoreCase),
				}));
		}
	}
}
