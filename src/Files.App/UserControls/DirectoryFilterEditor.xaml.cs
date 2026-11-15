// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Files.App.UserControls
{
	/// <summary>
	/// Inline editor for a single filter condition. Values are written to the view model immediately.
	/// </summary>
	public sealed partial class DirectoryFilterEditor : UserControl, INotifyPropertyChanged
	{
		private bool isSyncingMode;
		private bool isSyncingUi;
		private DirectoryFilterViewModel viewModel = new(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);

		public DirectoryFilterEditor()
		{
			InitializeComponent();
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public DirectoryFilterViewModel ViewModel
		{
			get => viewModel;
			set
			{
				if (ReferenceEquals(viewModel, value))
					return;

				viewModel.PropertyChanged -= ViewModel_PropertyChanged;

				// Item sources change while the previous view model is still the binding target, so a
				// selection reset cannot write into the incoming condition. Identical lists are reused
				// to avoid the reset in the first place.
				SetItemSource(OperatorCombo, value.OperatorChoices);
				SetItemSource(ScopeCombo, value.ScopeChoices);
				SetItemSource(RegexTargetCombo, value.RegexTargetChoices);
				SetItemSource(SizeUnitCombo, value.SizeUnitChoices);
				SetItemSource(DatePresetCombo, value.DatePresetChoices);

				viewModel = value;
				viewModel.PropertyChanged += ViewModel_PropertyChanged;

				ApplyViewModel();

				OnPropertyChanged();
				OnPropertyChanged(nameof(IsExtensionSectionVisible));
				OnPropertyChanged(nameof(IsChoiceListVisible));
				OnPropertyChanged(nameof(IsIncludeEmptyVisible));
				OnPropertyChanged(nameof(LengthValue));
			}
		}

		public double LengthValue
		{
			get => ViewModel.Length ?? double.NaN;
			set => ViewModel.Length = double.IsNaN(value) ? null : (int)Math.Round(value, MidpointRounding.AwayFromZero);
		}

		/// <summary>
		/// Extension choices are a separate condition in the bar, so only the extension editor shows them.
		/// </summary>
		public bool IsExtensionSectionVisible
			=> ViewModel.ShowsExtensionChoices && ViewModel.Column is DirectoryColumnKind.Extension;

		/// <summary>
		/// Extensions are edited by their own condition, so only the shared "empty" option belongs to the list panel.
		/// </summary>
		public bool IsChoiceListVisible => ViewModel.Column is not DirectoryColumnKind.Extension;

		/// <summary>
		/// The inline Type condition cannot carry "include empty" because its extension part is hidden.
		/// </summary>
		public bool IsIncludeEmptyVisible => ViewModel.Column is not DirectoryColumnKind.Type;

		public void FocusFirstInput()
		{
			if (ViewModel.IsTextColumn)
			{
				if (ViewModel.UseRegex)
					RegexPatternBox.Focus(FocusState.Programmatic);
				else if (ViewModel.IsLengthOperator)
					LengthBox.Focus(FocusState.Programmatic);
				else
					FilterTextBox.Focus(FocusState.Programmatic);
			}
			else if (ViewModel.IsChoiceColumn)
				ChoicesList.Focus(FocusState.Programmatic);
			else if (ViewModel.IsSizeColumn)
				MinSizeBox.Focus(FocusState.Programmatic);
			else if (ViewModel.IsDateColumn)
				MinDatePicker.Focus(FocusState.Programmatic);
		}

		private void ApplyViewModel()
		{
			isSyncingUi = true;

			try
			{
				ChoicesList.ItemsSource = ViewModel.Choices;
				ExtensionList.ItemsSource = ViewModel.ExtensionChoices;

				DatePresetCombo.SelectedIndex = Math.Clamp(ViewModel.DatePresetIndex, 0, ViewModel.DatePresetChoices.Length - 1);
				RegexIgnoreCaseCheck.IsChecked = !ViewModel.RegexIgnoreCase;
				SyncMode();
				UpdateRegexError();
			}
			finally
			{
				isSyncingUi = false;
			}
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(DirectoryFilterViewModel.UseRegex):
					SyncMode();
					UpdateRegexError();
					break;

				case nameof(DirectoryFilterViewModel.RegexPattern):
					UpdateRegexError();
					break;

				case nameof(DirectoryFilterViewModel.RegexIgnoreCase):
					SyncIgnoreCase();
					break;

				case nameof(DirectoryFilterViewModel.Length):
					OnPropertyChanged(nameof(LengthValue));
					break;

				case nameof(DirectoryFilterViewModel.Choices):
					ChoicesList.ItemsSource = ViewModel.Choices;
					break;

				case nameof(DirectoryFilterViewModel.ExtensionChoices):
					ExtensionList.ItemsSource = ViewModel.ExtensionChoices;
					break;
			}
		}

		private void SyncMode()
		{
			isSyncingMode = true;

			try
			{
				var useRegex = ViewModel.UseRegex;

				MatchModeSelector.SelectedItem = useRegex ? RegexModeItem : TextModeItem;
				TextModeItem.IsSelected = !useRegex;
				RegexModeItem.IsSelected = useRegex;
			}
			finally
			{
				isSyncingMode = false;
			}
		}

		private void SyncIgnoreCase()
		{
			isSyncingUi = true;

			try
			{
				RegexIgnoreCaseCheck.IsChecked = !ViewModel.RegexIgnoreCase;
			}
			finally
			{
				isSyncingUi = false;
			}
		}

		private void UpdateRegexError()
		{
			if (!ViewModel.UseRegex || string.IsNullOrWhiteSpace(ViewModel.RegexPattern) || ViewModel.ValidateRegex())
			{
				RegexErrorText.Text = string.Empty;
				RegexErrorText.Visibility = Visibility.Collapsed;
				return;
			}

			RegexErrorText.Text = ViewModel.RegexError;
			RegexErrorText.Visibility = Visibility.Visible;
		}

		private void MatchMode_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
		{
			if (isSyncingMode)
				return;

			ViewModel.UseRegex = MatchModeSelector.SelectedItem == RegexModeItem;
		}

		private void RegexIgnoreCase_Changed(object sender, RoutedEventArgs e)
		{
			if (isSyncingUi)
				return;

			ViewModel.RegexIgnoreCase = RegexIgnoreCaseCheck.IsChecked != true;
		}

		private void DatePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (isSyncingUi || DatePresetCombo.SelectedIndex < 0)
				return;

			var preset = (DirectoryDatePreset)DatePresetCombo.SelectedIndex;
			ViewModel.DatePresetIndex = DatePresetCombo.SelectedIndex;

			if (preset is DirectoryDatePreset.None)
			{
				ViewModel.MinDate = null;
				ViewModel.MaxDate = null;
			}
			else if (preset is not DirectoryDatePreset.Custom)
			{
				ViewModel.ApplyDatePreset(preset);
			}
		}

		private static void SetItemSource(ItemsControl control, IReadOnlyList<string> items)
		{
			if (control.ItemsSource is IReadOnlyList<string> current && current.SequenceEqual(items))
				return;

			control.ItemsSource = items;
		}

		private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
