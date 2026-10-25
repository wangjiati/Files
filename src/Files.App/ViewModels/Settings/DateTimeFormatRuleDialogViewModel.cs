// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;

namespace Files.App.ViewModels.Settings
{
	/// <summary>
	/// Edits one directory scoped date time format rule in the folder rule dialog.
	/// </summary>
	public sealed partial class DateTimeFormatRuleDialogViewModel : ObservableObject
	{
		private readonly DateTimeFormatSettingsViewModel _settings;

		private string _directoryPath;
		private int _selectedScopeIndex;
		private int _selectedFormatIndex;
		private string _customPattern;
		private bool _includeSubdirectories;
		private bool _showValidation;

		public DateTimeFormatRuleDialogViewModel(
			DateTimeFormatSettingsViewModel settings,
			DateTimeFormatRule? existingRule = null,
			string? initialDirectoryPath = null,
			DateTimeColumnKind? initialColumn = null)
		{
			_settings = settings;
			ExistingRule = existingRule;

			_directoryPath = existingRule?.DirectoryPath ?? initialDirectoryPath ?? string.Empty;
			_selectedScopeIndex = DateTimeFormatSettingsViewModel.GetScopeChoiceIndex(existingRule?.Column ?? initialColumn);

			var format = existingRule?.Format ?? settings.SelectedGlobalFormat;
			_selectedFormatIndex = DateTimeFormatSettingsViewModel.GetFormatChoiceIndex(format);
			_customPattern = existingRule?.CustomPattern ?? (format is DateTimeFormats.Custom ? settings.GlobalCustomPattern : string.Empty);
			_includeSubdirectories = existingRule?.IncludeSubdirectories ?? false;
		}

		public DateTimeFormatRule? ExistingRule { get; }

		public bool HasExistingRule
			=> ExistingRule is not null;

		public bool CanDelete
			=> ExistingRule is not null;

		public string DialogTitle
			=> ExistingRule is null
				? Strings.DateTimeFormatRuleAddTitle.GetLocalizedResource()
				: Strings.DateTimeFormatRuleEditTitle.GetLocalizedResource();

		public ObservableCollection<DateTimeFormatChoice> FormatChoices
			=> _settings.FormatChoices;

		public ObservableCollection<DateTimeFormatScopeChoice> ScopeChoices
			=> _settings.DirectoryScopeChoices;

		public string DirectoryPath
		{
			get => _directoryPath;
			set
			{
				if (!SetProperty(ref _directoryPath, value ?? string.Empty))
					return;

				_showValidation = true;
				NotifyValidationAndPreview();
			}
		}

		public int SelectedScopeIndex
		{
			get => _selectedScopeIndex;
			set
			{
				if (!SetProperty(ref _selectedScopeIndex, value))
					return;

				OnPropertyChanged(nameof(SelectedColumn));
			}
		}

		public DateTimeColumnKind? SelectedColumn
			=> DateTimeFormatSettingsViewModel.GetColumnForScopeChoiceIndex(SelectedScopeIndex);

		public int SelectedFormatIndex
		{
			get => _selectedFormatIndex;
			set
			{
				if (!SetProperty(ref _selectedFormatIndex, value))
					return;

				OnPropertyChanged(nameof(SelectedFormat));
				_showValidation = true;
				NotifyValidationAndPreview();
			}
		}

		public DateTimeFormats SelectedFormat
			=> DateTimeFormatSettingsViewModel.GetFormatForChoiceIndex(SelectedFormatIndex);

		public string CustomPattern
		{
			get => _customPattern;
			set
			{
				if (!SetProperty(ref _customPattern, value ?? string.Empty))
					return;

				_showValidation = true;
				NotifyValidationAndPreview();
			}
		}

		public bool ShowCustomPattern
			=> SelectedFormat is DateTimeFormats.Custom;

		public bool IncludeSubdirectories
		{
			get => _includeSubdirectories;
			set => SetProperty(ref _includeSubdirectories, value);
		}

		public string PreviewText
			=> _settings.GetPreview(SelectedFormat, CustomPattern);

		public DateTimeFormatRuleValidationError ValidationError
			=> _settings.ValidateDirectoryRule(DirectoryPath, SelectedFormat, CustomPattern);

		public bool HasValidationError
			=> _showValidation && ValidationError is not DateTimeFormatRuleValidationError.None;

		public string ValidationErrorMessage
			=> HasValidationError ? DateTimeFormatValidationMessages.GetMessage(ValidationError) : string.Empty;

		public bool Save()
		{
			_showValidation = true;

			var error = _settings.SaveDirectoryRule(
				ExistingRule,
				DirectoryPath,
				SelectedColumn,
				SelectedFormat,
				CustomPattern,
				IncludeSubdirectories);

			NotifyValidationAndPreview();

			return error is DateTimeFormatRuleValidationError.None;
		}

		public bool Delete()
		{
			if (ExistingRule is null)
				return false;

			_settings.DeleteDirectoryRule(ExistingRule);
			return true;
		}

		private void NotifyValidationAndPreview()
		{
			OnPropertyChanged(nameof(ShowCustomPattern));
			OnPropertyChanged(nameof(PreviewText));
			OnPropertyChanged(nameof(ValidationError));
			OnPropertyChanged(nameof(HasValidationError));
			OnPropertyChanged(nameof(ValidationErrorMessage));
		}
	}
}
