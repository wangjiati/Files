// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.UserControls
{
	/// <summary>
	/// Column filter editor hosted by a flyout in the details header.
	/// The flyout is populated explicitly because its content is not loaded until first shown.
	/// </summary>
	public sealed partial class DirectoryFilterFlyout : UserControl
	{
		private bool isInitialized;
		private bool isSyncingMode;

		public DirectoryFilterFlyout()
		{
			InitializeComponent();
		}

		public event EventHandler? ApplyRequested;

		public event EventHandler? ClearRequested;

		public event EventHandler? ClearAllRequested;

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
			RegexTargetCombo.ItemsSource = viewModel.RegexTargetChoices.ToArray();
			RegexTargetCombo.SelectedIndex = viewModel.RegexTargetIndex;
			RegexIgnoreCaseCheck.IsChecked = !viewModel.RegexIgnoreCase;
			RegexPatternBox.Text = viewModel.RegexPattern ?? string.Empty;
			RegexErrorText.Visibility = Visibility.Collapsed;

			ChoicePanel.Visibility = ToVisibility(viewModel.IsChoiceColumn);
			ChoicesList.ItemsSource = viewModel.Choices;
			IncludeEmptyCheck.IsChecked = viewModel.IncludeEmpty;

			ExtensionPanel.Visibility = ToVisibility(viewModel.ShowsExtensionChoices);
			ExtensionList.ItemsSource = viewModel.ExtensionChoices;

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

		private static Visibility ToVisibility(bool value)
			=> value ? Visibility.Visible : Visibility.Collapsed;

		private void DatePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// Ignore the programmatic selection applied while the flyout is being populated.
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
			// Ignore the programmatic selection applied while the flyout is being populated.
			if (!isInitialized || isSyncingMode)
				return;

			var useRegex = MatchModeSelector.SelectedItem == RegexModeItem || RegexModeItem.IsSelected;
			ViewModel.UseRegex = useRegex;
			TextMatchPanel.Visibility = ToVisibility(!useRegex);
			RegexOptionsPanel.Visibility = ToVisibility(useRegex);
			RegexErrorText.Visibility = Visibility.Collapsed;
		}

		private void Apply_Click(object sender, RoutedEventArgs e)
			=> ApplyFilter();

		private void TextInput_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key is not VirtualKey.Enter)
				return;

			e.Handled = true;
			ApplyFilter();
		}

		private void ApplyFilter()
		{
			ViewModel.OperatorIndex = Math.Max(OperatorCombo.SelectedIndex, 0);
			ViewModel.Text = FilterTextBox.Text;
			ViewModel.IncludeEmpty = IncludeEmptyCheck.IsChecked == true;
			ViewModel.SizeUnitIndex = Math.Max(SizeUnitCombo.SelectedIndex, 0);
			ViewModel.MinSizeText = MinSizeBox.Text;
			ViewModel.MaxSizeText = MaxSizeBox.Text;
			ViewModel.MinDate = MinDatePicker.Date;
			ViewModel.MaxDate = MaxDatePicker.Date;
			ViewModel.UseRegex = MatchModeSelector.SelectedItem == RegexModeItem;
			ViewModel.RegexTargetIndex = Math.Max(RegexTargetCombo.SelectedIndex, 0);
			ViewModel.RegexIgnoreCase = RegexIgnoreCaseCheck.IsChecked != true;
			ViewModel.RegexPattern = RegexPatternBox.Text;

			if (!ViewModel.ValidateRegex())
			{
				RegexErrorText.Text = ViewModel.RegexError;
				RegexErrorText.Visibility = Visibility.Visible;
				return;
			}

			RegexErrorText.Visibility = Visibility.Collapsed;
			ApplyRequested?.Invoke(this, EventArgs.Empty);
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
			=> ClearRequested?.Invoke(this, EventArgs.Empty);

		private void ClearAll_Click(object sender, RoutedEventArgs e)
			=> ClearAllRequested?.Invoke(this, EventArgs.Empty);
	}
}
