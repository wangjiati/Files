// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using System;
using System.Collections.Generic;
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
	/// Edits one column condition of the shared <see cref="DirectoryFilterSpec"/> without touching other columns.
	/// The name column writes the Contains operator into the shared name box instead of adding a duplicate condition.
	/// </summary>
	public sealed partial class DirectoryFilterViewModel : ObservableObject
	{
		private static readonly long[] SizeUnitMultipliers = [1, 1024, 1024L * 1024, 1024L * 1024 * 1024];

		private readonly DirectoryColumnKind column;
		private readonly Func<DateTimeOffset> clock;
		private readonly Func<string, string> localize;
		private readonly List<string> seededValues = [];
		private readonly List<string> seededExtensions = [];

		[ObservableProperty]
		private string? text;

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
		private int regexTargetIndex;

		[ObservableProperty]
		private bool regexIgnoreCase = true;

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
			DirectoryColumnKind.Tags => Strings.Tag,
			DirectoryColumnKind.Size => Strings.Size,
			DirectoryColumnKind.ModifiedDate => Strings.DateModifiedLowerCase,
			DirectoryColumnKind.CreatedDate => Strings.DateCreated,
			_ => Strings.Name,
		});

		public bool IsTextColumn => column is DirectoryColumnKind.Name;

		public bool IsNameColumn => column is DirectoryColumnKind.Name;

		public bool IsChoiceColumn => column is DirectoryColumnKind.Type or DirectoryColumnKind.Tags;

		public bool ShowsExtensionChoices => column is DirectoryColumnKind.Type;

		public bool IsSizeColumn => column is DirectoryColumnKind.Size;

		public bool IsDateColumn => column is DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate;

		public IReadOnlyList<string> OperatorChoices =>
		[
			localize(Strings.FilterContains),
			localize(Strings.FilterNotContains),
			localize(Strings.FilterStartsWith),
			localize(Strings.FilterEndsWith),
			localize(Strings.FilterEquals),
		];

		public IReadOnlyList<string> SizeUnitChoices { get; } = ["B", "KB", "MB", "GB"];

		public IReadOnlyList<string> DatePresetChoices =>
		[
			localize(Strings.FilterAllDates),
			localize(Strings.Today),
			localize(Strings.FilterLast7Days),
			localize(Strings.FilterLast30Days),
			localize(Strings.FilterThisYear),
		];

		public IReadOnlyList<string> RegexTargetChoices =>
		[
			localize(Strings.FilterTargetRawName),
			localize(Strings.FilterTargetDisplayName),
			localize(Strings.FilterTargetFullPath),
		];

		/// <summary>
		/// Validation message for the current regex; null when it can be applied.
		/// </summary>
		public string? RegexError { get; private set; }

		public ObservableCollection<DirectoryFilterValueOption> Choices { get; private set; } = [];

		public ObservableCollection<DirectoryFilterValueOption> ExtensionChoices { get; private set; } = [];

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

		public DirectoryFilterSpec ApplyTo(DirectoryFilterSpec spec)
		{
			var columns = spec.Columns.ToList();

			switch (column)
			{
				case DirectoryColumnKind.Name:
					{
						columns.RemoveAll(filter => filter.Column == DirectoryColumnKind.Name);

						// Text and regex are alternative modes; the hidden mode must never leak into the spec.
						if (UseRegex)
						{
							var regex = !string.IsNullOrWhiteSpace(RegexPattern) ? BuildRegexOptions() : null;

							return spec with { NameContains = null, Columns = columns, Regex = regex };
						}

						var condition = BuildNameCondition();

						if (condition is null)
							return spec with { NameContains = null, Columns = columns, Regex = null };

						// Keep a single source of truth: a plain Contains lives in the shared name box.
						if (condition.Operator is DirectoryTextOperator.Contains)
							return spec with { NameContains = condition.Text, Columns = columns, Regex = null };

						columns.Add(condition);
						return spec with { NameContains = null, Columns = columns, Regex = null };
					}

				case DirectoryColumnKind.Type:
					{
						columns.RemoveAll(filter => filter.Column is DirectoryColumnKind.Type or DirectoryColumnKind.Extension);

						if (BuildChoiceCondition(DirectoryColumnKind.Type, Choices, includeEmpty: false) is { } typeCondition)
							columns.Add(typeCondition);

						if (BuildChoiceCondition(DirectoryColumnKind.Extension, ExtensionChoices, IncludeEmpty) is { } extensionCondition)
							columns.Add(extensionCondition);

						return spec with { Columns = columns };
					}

				case DirectoryColumnKind.Tags:
					{
						columns.RemoveAll(filter => filter.Column == DirectoryColumnKind.Tags);

						if (BuildChoiceCondition(DirectoryColumnKind.Tags, Choices, IncludeEmpty) is { } tagCondition)
							columns.Add(tagCondition);

						return spec with { Columns = columns };
					}

				case DirectoryColumnKind.Size:
					columns.RemoveAll(filter => filter.Column == DirectoryColumnKind.Size);

					if (BuildSizeCondition() is { } sizeCondition)
						columns.Add(sizeCondition);

					return spec with { Columns = columns };

				case DirectoryColumnKind.ModifiedDate:
				case DirectoryColumnKind.CreatedDate:
					columns.RemoveAll(filter => filter.Column == column);

					if (MinDate is not null || MaxDate is not null)
					{
						columns.Add(new DirectoryColumnFilter
						{
							Column = column,
							MinDate = MinDate,
							MaxDate = MaxDate,
						});
					}

					return spec with { Columns = columns };

				default:
					return spec;
			}
		}

		public DirectoryFilterSpec ClearFrom(DirectoryFilterSpec spec)
		{
			var columns = spec.Columns
				.Where(filter => column switch
				{
					DirectoryColumnKind.Name => filter.Column != DirectoryColumnKind.Name,
					DirectoryColumnKind.Type => filter.Column is not (DirectoryColumnKind.Type or DirectoryColumnKind.Extension),
					_ => filter.Column != column,
				})
				.ToList();

			return spec with
			{
				NameContains = column is DirectoryColumnKind.Name ? null : spec.NameContains,
				Columns = columns,
				Regex = column is DirectoryColumnKind.Name ? null : spec.Regex,
			};
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
				(DirectoryRegexTarget)Math.Clamp(RegexTargetIndex, 0, 2),
				RegexIgnoreCase);

		private DirectoryColumnFilter? BuildNameCondition()
		{
			var value = Text?.Trim();

			if (string.IsNullOrEmpty(value))
				return null;

			return new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Name,
				Operator = (DirectoryTextOperator)Math.Clamp(OperatorIndex, 0, 4),
				Text = value,
			};
		}

		private DirectoryColumnFilter? BuildChoiceCondition(DirectoryColumnKind kind, IReadOnlyList<DirectoryFilterValueOption> options, bool includeEmpty)
		{
			var values = options.Where(option => option.IsSelected).Select(option => option.Value).ToList();

			if (values.Count == 0 && !includeEmpty)
				return null;

			return new DirectoryColumnFilter
			{
				Column = kind,
				Values = values,
				IncludeEmpty = includeEmpty,
			};
		}

		private DirectoryColumnFilter? BuildSizeCondition()
		{
			var min = ToBytes(MinSizeText);
			var max = ToBytes(MaxSizeText);

			if (min is null && max is null)
				return null;

			return new DirectoryColumnFilter
			{
				Column = DirectoryColumnKind.Size,
				MinSize = min,
				MaxSize = max,
			};
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
						RegexTargetIndex = (int)regex.Target;
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
