// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;

namespace Files.App.ViewModels.Settings
{
	/// <summary>
	/// Represents a selectable date time format in the settings UI.
	/// </summary>
	public sealed class DateTimeFormatChoice
	{
		internal DateTimeFormatChoice(DateTimeFormats? format, string resourceKey)
		{
			Format = format;
			ResourceKey = resourceKey;
		}

		public DateTimeFormats? Format { get; }

		internal string ResourceKey { get; }

		public string Label => ResourceKey.GetLocalizedResource();

		public bool IsCustom => Format is DateTimeFormats.Custom;

		public bool IsInherit => Format is null;
	}

	/// <summary>
	/// Represents a selectable directory rule scope in the settings UI.
	/// </summary>
	public sealed class DateTimeFormatScopeChoice
	{
		internal DateTimeFormatScopeChoice(DateTimeColumnKind? column, string resourceKey)
		{
			Column = column;
			ResourceKey = resourceKey;
		}

		public DateTimeColumnKind? Column { get; }

		internal string ResourceKey { get; }

		public string Label => ResourceKey.GetLocalizedResource();
	}

	/// <summary>
	/// Formats validation errors for display in the settings UI.
	/// </summary>
	public static class DateTimeFormatValidationMessages
	{
		public static string GetMessage(DateTimeFormatRuleValidationError error)
			=> error switch
			{
				DateTimeFormatRuleValidationError.None => string.Empty,
				DateTimeFormatRuleValidationError.MissingPattern => Strings.DateTimeFormatPatternRequired.GetLocalizedResource(),
				DateTimeFormatRuleValidationError.InvalidPattern => Strings.DateTimeFormatPatternInvalid.GetLocalizedResource(),
				DateTimeFormatRuleValidationError.MissingDirectory => Strings.DateTimeFormatDirectoryRequired.GetLocalizedResource(),
				_ => Strings.DateTimeFormatRuleInvalid.GetLocalizedResource(),
			};
	}

	/// <summary>
	/// Provides the global and directory scoped date time format editing model used by the general settings page
	/// and the folder rule dialog.
	/// </summary>
	public sealed partial class DateTimeFormatSettingsViewModel : ObservableObject
	{
		private static readonly DateTimeOffset PreviewSampleDate =
			new(new DateTime(2024, 12, 31, 14, 30, 5, 123, DateTimeKind.Local));

		private readonly IGeneralSettingsService _settings;
		private readonly IDateTimeFormatterFactory _formatterFactory;
		private readonly IVolumePathProvider? _volumePathProvider;

		private int _selectedGlobalFormatIndex;
		private string _globalCustomPattern = string.Empty;
		private DateTimeFormatRuleValidationError _globalValidationError;

		public DateTimeFormatSettingsViewModel(
			IGeneralSettingsService settings,
			IDateTimeFormatterFactory formatterFactory,
			IVolumePathProvider? volumePathProvider = null)
		{
			_settings = settings;
			_formatterFactory = formatterFactory;
			_volumePathProvider = volumePathProvider;

			FormatChoices =
			[
				new(DateTimeFormats.Application, nameof(Strings.Application)),
				new(DateTimeFormats.System, nameof(Strings.SystemTimeStyle)),
				new(DateTimeFormats.Universal, nameof(Strings.Universal)),
				new(DateTimeFormats.Custom, nameof(Strings.Custom)),
			];

			ColumnFormatChoices =
			[
				new(null, nameof(Strings.DateTimeFormatUseGlobalDefault)),
				.. FormatChoices,
			];

			DirectoryScopeChoices =
			[
				new(null, nameof(Strings.DateTimeFormatColumnDefault)),
				new(DateTimeColumnKind.Modified, nameof(Strings.DateModifiedLowerCase)),
				new(DateTimeColumnKind.Created, nameof(Strings.DateCreated)),
				new(DateTimeColumnKind.Deleted, nameof(Strings.DateDeleted)),
			];

			_selectedGlobalFormatIndex = GetFormatChoiceIndex(settings.DateTimeFormat);
			_globalCustomPattern = settings.DateTimeFormatCustomPattern ?? string.Empty;
			_globalValidationError = ValidateFormat(settings.DateTimeFormat, _globalCustomPattern);

			ModifiedRule = new(this, DateTimeColumnKind.Modified);
			CreatedRule = new(this, DateTimeColumnKind.Created);
			DeletedRule = new(this, DateTimeColumnKind.Deleted);
			ColumnRules = [ModifiedRule, CreatedRule, DeletedRule];

			DirectoryRules = [];
			RefreshDirectoryRules();
		}

		public ObservableCollection<DateTimeFormatChoice> FormatChoices { get; }

		public ObservableCollection<DateTimeFormatChoice> ColumnFormatChoices { get; }

		public ObservableCollection<DateTimeFormatScopeChoice> DirectoryScopeChoices { get; }

		public DateTimeFormatColumnRuleViewModel ModifiedRule { get; }

		public DateTimeFormatColumnRuleViewModel CreatedRule { get; }

		public DateTimeFormatColumnRuleViewModel DeletedRule { get; }

		public IReadOnlyList<DateTimeFormatColumnRuleViewModel> ColumnRules { get; }

		public ObservableCollection<DateTimeFormatDirectoryRuleViewModel> DirectoryRules { get; }

		public int SelectedGlobalFormatIndex
		{
			get => _selectedGlobalFormatIndex;
			set
			{
				if (!SetProperty(ref _selectedGlobalFormatIndex, value))
					return;

				OnPropertyChanged(nameof(SelectedGlobalFormat));
				OnPropertyChanged(nameof(ShowGlobalCustomPattern));
				OnPropertyChanged(nameof(GlobalPreview));
				SaveGlobalFormat(SelectedGlobalFormat, GlobalCustomPattern);
			}
		}

		public DateTimeFormats SelectedGlobalFormat
			=> GetFormatForChoiceIndex(SelectedGlobalFormatIndex);

		public string GlobalCustomPattern
		{
			get => _globalCustomPattern;
			set
			{
				if (!SetProperty(ref _globalCustomPattern, value ?? string.Empty))
					return;

				OnPropertyChanged(nameof(GlobalPreview));
				if (SelectedGlobalFormat is DateTimeFormats.Custom)
					SaveGlobalFormat(SelectedGlobalFormat, GlobalCustomPattern);
			}
		}

		public bool ShowGlobalCustomPattern
			=> SelectedGlobalFormat is DateTimeFormats.Custom;

		public string GlobalPreview
			=> GetPreview(SelectedGlobalFormat, GlobalCustomPattern);

		public DateTimeFormatRuleValidationError GlobalValidationError
		{
			get => _globalValidationError;
			private set
			{
				if (SetProperty(ref _globalValidationError, value))
					OnPropertyChanged(nameof(GlobalValidationErrorMessage));
			}
		}

		public bool HasGlobalValidationError
			=> GlobalValidationError is not DateTimeFormatRuleValidationError.None;

		public string GlobalValidationErrorMessage
			=> DateTimeFormatValidationMessages.GetMessage(GlobalValidationError);

		public static DateTimeFormats GetFormatForChoiceIndex(int index)
			=> index switch
			{
				1 => DateTimeFormats.System,
				2 => DateTimeFormats.Universal,
				3 => DateTimeFormats.Custom,
				_ => DateTimeFormats.Application,
			};

		public static int GetFormatChoiceIndex(DateTimeFormats format)
			=> format switch
			{
				DateTimeFormats.System => 1,
				DateTimeFormats.Universal => 2,
				DateTimeFormats.Custom => 3,
				_ => 0,
			};

		public static DateTimeFormats? GetFormatForColumnChoiceIndex(int index)
			=> index switch
			{
				0 => null,
				1 => DateTimeFormats.Application,
				2 => DateTimeFormats.System,
				3 => DateTimeFormats.Universal,
				4 => DateTimeFormats.Custom,
				_ => null,
			};

		public static int GetColumnFormatChoiceIndex(DateTimeFormats? format)
			=> format switch
			{
				null => 0,
				DateTimeFormats.Application => 1,
				DateTimeFormats.System => 2,
				DateTimeFormats.Universal => 3,
				DateTimeFormats.Custom => 4,
				_ => 0,
			};

		public static DateTimeColumnKind? GetColumnForScopeChoiceIndex(int index)
			=> index switch
			{
				1 => DateTimeColumnKind.Modified,
				2 => DateTimeColumnKind.Created,
				3 => DateTimeColumnKind.Deleted,
				_ => null,
			};

		public static int GetScopeChoiceIndex(DateTimeColumnKind? column)
			=> column switch
			{
				DateTimeColumnKind.Modified => 1,
				DateTimeColumnKind.Created => 2,
				DateTimeColumnKind.Deleted => 3,
				_ => 0,
			};

		public string GetPreview(DateTimeFormats format, string? customPattern)
			=> GetPreview(format, customPattern, PreviewSampleDate);

		public string GetPreview(DateTimeFormats format, string? customPattern, DateTimeOffset sample)
		{
			if (ValidateFormat(format, customPattern) is not DateTimeFormatRuleValidationError.None)
				return string.Empty;

			try
			{
				return _formatterFactory.GetDateTimeFormatter(format, customPattern).ToShortLabel(sample);
			}
			catch (Exception ex) when (ex is ArgumentException or FormatException)
			{
				return string.Empty;
			}
		}

		public DateTimeFormatRuleValidationError ValidateFormat(DateTimeFormats format, string? customPattern)
		{
			if (!Enum.IsDefined(format))
				return DateTimeFormatRuleValidationError.InvalidFormat;

			return format is DateTimeFormats.Custom
				? CustomDateTimeFormatter.ValidatePattern(customPattern)
				: DateTimeFormatRuleValidationError.None;
		}

		public DateTimeFormatRuleValidationError SaveGlobalFormat(DateTimeFormats format, string? customPattern)
		{
			var error = ValidateFormat(format, customPattern);
			GlobalValidationError = error;

			if (error is DateTimeFormatRuleValidationError.None)
			{
				_settings.DateTimeFormat = format;
				_settings.DateTimeFormatCustomPattern = format is DateTimeFormats.Custom ? customPattern : _settings.DateTimeFormatCustomPattern;
			}

			return error;
		}

		public DateTimeFormatRuleValidationError SaveColumnRule(
			DateTimeColumnKind column,
			DateTimeFormats? format,
			string? customPattern)
		{
			var ruleViewModel = GetColumnRule(column);
			var error = format is null
				? DateTimeFormatRuleValidationError.None
				: ValidateFormat(format.Value, customPattern);

			if (error is DateTimeFormatRuleValidationError.None)
			{
				var rules = _settings.DateTimeColumnFormatRules
					.Where(rule => rule.Column != column)
					.ToList();

				if (format is not null)
				{
					rules.Add(new DateTimeFormatRule(
						column,
						null,
						false,
						format.Value,
						format is DateTimeFormats.Custom ? customPattern : null));
				}

				_settings.DateTimeColumnFormatRules = rules;
				ruleViewModel?.ApplyStoredValue(format, customPattern);
			}

			ruleViewModel?.SetValidationError(error);

			return error;
		}

		public DateTimeFormatRuleValidationError ValidateDirectoryRule(
			string directoryPath,
			DateTimeFormats format,
			string? customPattern)
		{
			if (string.IsNullOrWhiteSpace(directoryPath))
				return DateTimeFormatRuleValidationError.MissingDirectory;

			return ValidateFormat(format, customPattern);
		}

		public DateTimeFormatRuleValidationError SaveDirectoryRule(
			DateTimeFormatRule? existingRule,
			string directoryPath,
			DateTimeColumnKind? column,
			DateTimeFormats format,
			string? customPattern,
			bool includeSubdirectories)
		{
			var error = ValidateDirectoryRule(directoryPath, format, customPattern);

			if (error is not DateTimeFormatRuleValidationError.None)
				return error;

			var normalizedDirectoryPath = PortableDirectoryKey.NormalizePath(directoryPath);
			var directoryChanged = existingRule is null ||
				!string.Equals(
					PortableDirectoryKey.NormalizePath(existingRule.DirectoryPath),
					normalizedDirectoryPath,
					StringComparison.OrdinalIgnoreCase);

			var rule = new DateTimeFormatRule(
				column,
				normalizedDirectoryPath,
				includeSubdirectories,
				format,
				format is DateTimeFormats.Custom ? customPattern : null);

			if (_volumePathProvider is not null)
			{
				// Only an explicit directory change may discard the stored volume identity.
				rule = PortableDirectoryKey.NormalizeRule(rule, _volumePathProvider, resetVolumeIdentity: directoryChanged);
			}
			else if (!directoryChanged && existingRule is not null)
			{
				rule = rule with
				{
					VolumeId = existingRule.VolumeId,
					RelativePath = existingRule.RelativePath,
				};
			}

			var rules = _settings.DateTimeFormatDirectoryRules.ToList();
			var existingIndex = existingRule is null ? -1 : FindRuleIndex(rules, existingRule);

			if (existingIndex < 0)
				existingIndex = rules.FindIndex(candidate => IsSameScope(candidate, rule));

			if (existingIndex >= 0)
				rules[existingIndex] = rule;
			else
				rules.Add(rule);

			_settings.DateTimeFormatDirectoryRules = rules;
			RefreshDirectoryRules();

			return DateTimeFormatRuleValidationError.None;
		}

		public DateTimeFormatRule? FindExactDirectoryRule(string directoryPath, DateTimeColumnKind? column)
		{
			var normalizedDirectoryPath = PortableDirectoryKey.NormalizePath(directoryPath);

			return _settings.DateTimeFormatDirectoryRules.FirstOrDefault(
				rule => rule.Column == column &&
					string.Equals(
						PortableDirectoryKey.NormalizePath(rule.DirectoryPath),
						normalizedDirectoryPath,
						StringComparison.OrdinalIgnoreCase));
		}

		public void DeleteDirectoryRule(DateTimeFormatRule rule)
		{
			var rules = _settings.DateTimeFormatDirectoryRules.ToList();
			var index = FindRuleIndex(rules, rule);

			if (index < 0)
				index = rules.FindIndex(candidate => IsSameScope(candidate, rule));

			if (index < 0)
				return;

			rules.RemoveAt(index);
			_settings.DateTimeFormatDirectoryRules = rules;
			RefreshDirectoryRules();
		}

		public void RefreshDirectoryRules()
		{
			DirectoryRules.Clear();

			foreach (var rule in _settings.DateTimeFormatDirectoryRules)
				DirectoryRules.Add(new DateTimeFormatDirectoryRuleViewModel(rule));

			OnPropertyChanged(nameof(HasDirectoryRules));
		}

		public bool HasDirectoryRules
			=> DirectoryRules.Count > 0;

		internal DateTimeFormatRule? FindStoredColumnRule(DateTimeColumnKind column)
			=> _settings.DateTimeColumnFormatRules.LastOrDefault(
				rule => rule.Column == column && string.IsNullOrWhiteSpace(rule.DirectoryPath));

		private DateTimeFormatColumnRuleViewModel? GetColumnRule(DateTimeColumnKind column)
			=> ColumnRules.FirstOrDefault(rule => rule.Column == column);

		private static int FindRuleIndex(IReadOnlyList<DateTimeFormatRule> rules, DateTimeFormatRule rule)
		{
			for (var index = 0; index < rules.Count; index++)
			{
				if (ReferenceEquals(rules[index], rule) || rules[index] == rule)
					return index;
			}

			return -1;
		}

		private static bool IsSameScope(DateTimeFormatRule left, DateTimeFormatRule right)
			=> left.Column == right.Column &&
				string.Equals(
					PortableDirectoryKey.NormalizePath(left.DirectoryPath),
					PortableDirectoryKey.NormalizePath(right.DirectoryPath),
					StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Edits one global per-column date time format rule.
	/// </summary>
	public sealed class DateTimeFormatColumnRuleViewModel : ObservableObject
	{
		private readonly DateTimeFormatSettingsViewModel _settings;
		private int _selectedFormatIndex;
		private string _customPattern = string.Empty;
		private DateTimeFormatRuleValidationError _validationError;

		internal DateTimeFormatColumnRuleViewModel(DateTimeFormatSettingsViewModel settings, DateTimeColumnKind column)
		{
			_settings = settings;
			Column = column;

			var rule = settings.FindStoredColumnRule(column);
			_selectedFormatIndex = DateTimeFormatSettingsViewModel.GetColumnFormatChoiceIndex(rule?.Format);
			_customPattern = rule?.CustomPattern ?? string.Empty;
			_validationError = rule?.Validate() ?? DateTimeFormatRuleValidationError.None;
		}

		public DateTimeColumnKind Column { get; }

		public string ColumnLabel
			=> Column switch
			{
				DateTimeColumnKind.Created => Strings.DateCreated.GetLocalizedResource(),
				DateTimeColumnKind.Deleted => Strings.DateDeleted.GetLocalizedResource(),
				_ => Strings.DateModifiedLowerCase.GetLocalizedResource(),
			};

		public ObservableCollection<DateTimeFormatChoice> FormatChoices
			=> _settings.ColumnFormatChoices;

		public DateTimeFormats? SelectedFormat
			=> DateTimeFormatSettingsViewModel.GetFormatForColumnChoiceIndex(SelectedFormatIndex);

		public int SelectedFormatIndex
		{
			get => _selectedFormatIndex;
			set
			{
				if (!SetProperty(ref _selectedFormatIndex, value))
					return;

				OnPropertyChanged(nameof(SelectedFormat));
				OnPropertyChanged(nameof(ShowCustomPattern));
				OnPropertyChanged(nameof(PreviewText));

				var error = _settings.SaveColumnRule(Column, SelectedFormat, CustomPattern);
				SetValidationError(error);
			}
		}

		public string CustomPattern
		{
			get => _customPattern;
			set
			{
				if (!SetProperty(ref _customPattern, value ?? string.Empty))
					return;

				OnPropertyChanged(nameof(PreviewText));

				if (SelectedFormat is DateTimeFormats.Custom)
				{
					var error = _settings.SaveColumnRule(Column, SelectedFormat, CustomPattern);
					SetValidationError(error);
				}
			}
		}

		public bool ShowCustomPattern
			=> SelectedFormat is DateTimeFormats.Custom;

		public string PreviewText
			=> SelectedFormat is { } format
				? _settings.GetPreview(format, CustomPattern)
				: _settings.GlobalPreview;

		public DateTimeFormatRuleValidationError ValidationError
		{
			get => _validationError;
			private set
			{
				if (SetProperty(ref _validationError, value))
				{
					OnPropertyChanged(nameof(HasValidationError));
					OnPropertyChanged(nameof(ValidationErrorMessage));
				}
			}
		}

		public bool HasValidationError
			=> ValidationError is not DateTimeFormatRuleValidationError.None;

		public string ValidationErrorMessage
			=> DateTimeFormatValidationMessages.GetMessage(ValidationError);

		internal void ApplyStoredValue(DateTimeFormats? format, string? customPattern)
		{
			_selectedFormatIndex = DateTimeFormatSettingsViewModel.GetColumnFormatChoiceIndex(format);
			_customPattern = customPattern ?? string.Empty;

			OnPropertyChanged(nameof(SelectedFormatIndex));
			OnPropertyChanged(nameof(SelectedFormat));
			OnPropertyChanged(nameof(CustomPattern));
			OnPropertyChanged(nameof(ShowCustomPattern));
			OnPropertyChanged(nameof(PreviewText));
		}

		internal void SetValidationError(DateTimeFormatRuleValidationError error)
			=> ValidationError = error;
	}

	/// <summary>
	/// Represents a stored directory rule in the settings list.
	/// </summary>
	public sealed class DateTimeFormatDirectoryRuleViewModel
	{
		public DateTimeFormatDirectoryRuleViewModel(DateTimeFormatRule rule)
		{
			Rule = rule;
		}

		public DateTimeFormatRule Rule { get; }

		public string DirectoryPath
			=> Rule.DirectoryPath ?? string.Empty;

		public string ScopeLabel
			=> Rule.Column switch
			{
				DateTimeColumnKind.Modified => Strings.DateModifiedLowerCase.GetLocalizedResource(),
				DateTimeColumnKind.Created => Strings.DateCreated.GetLocalizedResource(),
				DateTimeColumnKind.Deleted => Strings.DateDeleted.GetLocalizedResource(),
				_ => Strings.DateTimeFormatColumnDefault.GetLocalizedResource(),
			};

		public string FormatLabel
			=> Rule.Format is DateTimeFormats.Custom
				? string.Format(Strings.DateTimeFormatCustomValue.GetLocalizedResource(), Rule.CustomPattern)
				: Rule.Format switch
				{
					DateTimeFormats.System => Strings.SystemTimeStyle.GetLocalizedResource(),
					DateTimeFormats.Universal => Strings.Universal.GetLocalizedResource(),
					_ => Strings.Application.GetLocalizedResource(),
				};

		public string SubdirectoryLabel
			=> Rule.IncludeSubdirectories
				? Strings.DateTimeFormatIncludesSubfolders.GetLocalizedResource()
				: string.Empty;

		public string Description
			=> $"{ScopeLabel}: {FormatLabel}";
	}
}
