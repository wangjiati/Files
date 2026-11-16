// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.UserControls
{
	/// <summary>
	/// Vertical column filter form hosted by the column filter popup. The top bar keeps its
	/// compact inline editor; this form is shared by text, choice and date columns.
	/// </summary>
	public sealed partial class DirectoryFilterColumnEditor : UserControl
	{
		private bool isInitialized;
		private bool isSyncingMode;

		public DirectoryFilterColumnEditor()
		{
			InitializeComponent();
		}

		public event EventHandler? ApplyRequested;

		public DirectoryFilterViewModel ViewModel { get; private set; } = null!;

		public void Initialize(DirectoryFilterViewModel viewModel)
		{
			isInitialized = false;
			ViewModel = viewModel;

			TitleText.Text = viewModel.Title;

			TextPanel.Visibility = ToVisibility(viewModel.IsTextColumn);
			OperatorCombo.ItemsSource = viewModel.OperatorChoices.ToArray();
			OperatorCombo.SelectedIndex = viewModel.OperatorIndex;
			FilterTextBox.Text = viewModel.Text ?? string.Empty;

			TextModeItem.Text = Strings.FilterModeText.GetLocalizedResource();
			RegexModeItem.Text = Strings.FilterModeRegex.GetLocalizedResource();
			SyncMode();
			RegexPatternBox.Text = viewModel.RegexPattern ?? string.Empty;
			RegexErrorText.Visibility = Visibility.Collapsed;

			// Both options apply to text and regex alike.
			CaseCheck.IsChecked = viewModel.UseRegex ? !viewModel.RegexIgnoreCase : viewModel.CaseSensitive;
			ExtensionCheck.IsChecked = viewModel.UseRegex
				? viewModel.RegexTargetIndex == 0
				: viewModel.UseRawName;

			ChoicePanel.Visibility = ToVisibility(viewModel.IsChoiceColumn);
			ChoicesList.ItemsSource = viewModel.Choices;
			IncludeEmptyCheck.IsChecked = viewModel.IncludeEmpty;

			ExtensionPanel.Visibility = ToVisibility(viewModel.ShowsExtensionChoices);
			ExtensionList.ItemsSource = viewModel.ExtensionChoices;
			IncludeEmptyExtensionCheck.IsChecked = viewModel.IncludeEmpty;

			SizePanel.Visibility = ToVisibility(viewModel.IsSizeColumn);
			MinSizeBox.Text = viewModel.MinSizeText ?? string.Empty;
			SizeUnitCombo.ItemsSource = viewModel.SizeUnitChoices.ToArray();
			SizeUnitCombo.SelectedIndex = viewModel.SizeUnitIndex;
			MaxSizeBox.Text = viewModel.MaxSizeText ?? string.Empty;

			DatePanel.Visibility = ToVisibility(viewModel.IsDateColumn);
			DatePresetCombo.ItemsSource = viewModel.DatePresetChoices.ToArray();
			DatePresetCombo.SelectedIndex = viewModel.DatePresetIndex;
			MinDatePicker.Date = viewModel.MinDate;
			MaxDatePicker.Date = viewModel.MaxDate;

			isInitialized = true;
		}

		/// <summary>
		/// Copies the form values into the view model. Returns false when the regex is invalid.
		/// </summary>
		public bool Commit()
		{
			var caseSensitive = CaseCheck.IsChecked == true;
			var includeExtension = ExtensionCheck.IsChecked == true;

			ViewModel.OperatorIndex = Math.Max(OperatorCombo.SelectedIndex, 0);
			ViewModel.Text = FilterTextBox.Text;
			ViewModel.CaseSensitive = caseSensitive;
			ViewModel.RegexIgnoreCase = !caseSensitive;
			ViewModel.UseRawName = includeExtension;
			ViewModel.RegexTargetIndex = includeExtension ? 0 : 1;
			ViewModel.IncludeEmpty = IncludeEmptyCheck.IsChecked == true || IncludeEmptyExtensionCheck.IsChecked == true;
			ViewModel.SizeUnitIndex = Math.Max(SizeUnitCombo.SelectedIndex, 0);
			ViewModel.MinSizeText = MinSizeBox.Text;
			ViewModel.MaxSizeText = MaxSizeBox.Text;
			ViewModel.MinDate = MinDatePicker.Date;
			ViewModel.MaxDate = MaxDatePicker.Date;
			ViewModel.UseRegex = MatchModeSelector.SelectedItem == RegexModeItem;
			ViewModel.RegexPattern = RegexPatternBox.Text;

			if (!ViewModel.ValidateRegex())
			{
				RegexErrorText.Text = ViewModel.RegexError;
				RegexErrorText.Visibility = Visibility.Visible;
				return false;
			}

			RegexErrorText.Visibility = Visibility.Collapsed;
			return true;
		}

		private static Visibility ToVisibility(bool value)
			=> value ? Visibility.Visible : Visibility.Collapsed;

		private void SelectAll_Click(object sender, RoutedEventArgs e)
		{
			foreach (var option in ViewModel.Choices)
				option.IsSelected = true;

			foreach (var option in ViewModel.ExtensionChoices)
				option.IsSelected = true;
		}

		private void ClearSelection_Click(object sender, RoutedEventArgs e)
		{
			foreach (var option in ViewModel.Choices)
				option.IsSelected = false;

			foreach (var option in ViewModel.ExtensionChoices)
				option.IsSelected = false;
		}

		private void DatePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// Ignore the programmatic selection applied while the popup is being populated.
			if (!isInitialized)
				return;

			ViewModel.DatePresetIndex = DatePresetCombo.SelectedIndex;
			ViewModel.ApplyDatePreset();
			MinDatePicker.Date = ViewModel.MinDate;
			MaxDatePicker.Date = ViewModel.MaxDate;
		}

		/// <summary>
		/// Re-applies the tab selection and the visible editor. SelectorBar ignores a programmatic
		/// selection while its flyout is closed, so a fresh content instance is created for each open.
		/// </summary>
		private void SyncMode()
		{
			var useRegex = ViewModel.UseRegex;

			isSyncingMode = true;
			try
			{
				MatchModeSelector.SelectedItem = useRegex ? RegexModeItem : TextModeItem;

				if (useRegex)
				{
					TextModeItem.IsSelected = false;
					RegexModeItem.IsSelected = true;
				}
				else
				{
					RegexModeItem.IsSelected = false;
					TextModeItem.IsSelected = true;
				}

				TextMatchPanel.Visibility = ToVisibility(!useRegex);
				RegexOptionsPanel.Visibility = ToVisibility(useRegex);
			}
			finally
			{
				isSyncingMode = false;
			}
		}

		private void MatchMode_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
		{
			// Ignore the programmatic selection applied while the popup is being populated.
			if (!isInitialized || isSyncingMode)
				return;

			var useRegex = MatchModeSelector.SelectedItem == RegexModeItem;
			ViewModel.UseRegex = useRegex;
			TextMatchPanel.Visibility = ToVisibility(!useRegex);
			RegexOptionsPanel.Visibility = ToVisibility(useRegex);
			RegexErrorText.Visibility = Visibility.Collapsed;
		}

		private void TextInput_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key is not VirtualKey.Enter)
				return;

			e.Handled = true;
			if (Commit())
				ApplyRequested?.Invoke(this, EventArgs.Empty);
		}
	}
}
