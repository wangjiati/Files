// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.System;

namespace Files.App.UserControls
{
	/// <summary>
	/// Filter bar host: mode switch, keyword or name editor, the condition chain, recursion and the
	/// result count. The control only talks to <see cref="DirectoryFilterBarViewModel"/>; the shell
	/// supplies the view model through the data context and projects <c>BuildSpec</c>.
	/// </summary>
	public sealed partial class DirectoryFilterBar : UserControl, INotifyPropertyChanged
	{
		private static readonly int[] DepthPresets = [1, 2, 3, 4, 5, 64];

		public static readonly DependencyProperty ResultCountProperty = DependencyProperty.Register(
			nameof(ResultCount),
			typeof(int),
			typeof(DirectoryFilterBar),
			new PropertyMetadata(-1, OnResultCountChanged));

		private DirectoryFilterBarViewModel viewModel = new();
		private bool isSyncingDepth;
		private bool isSyncingMode;
		private bool focusLastAddedCondition;
		private Func<DirectoryColumnKind, IReadOnlyList<DirectoryFilterChoice>>? choiceProvider;

		public DirectoryFilterBar()
		{
			InitializeComponent();

			AttachViewModel(viewModel);
			DataContextChanged += DirectoryFilterBar_DataContextChanged;
			Loaded += DirectoryFilterBar_Loaded;
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public DirectoryFilterBarViewModel ViewModel
		{
			get => viewModel;
			private set
			{
				if (ReferenceEquals(viewModel, value))
					return;

				AttachViewModel(value);
			}
		}

		/// <summary>
		/// Matching item count supplied by the shell; a negative value hides the count.
		/// </summary>
		public int ResultCount
		{
			get => (int)GetValue(ResultCountProperty);
			set => SetValue(ResultCountProperty, value);
		}

		/// <summary>
		/// Supplies selectable values for choice conditions added by this control. The shell wires this to
		/// its filter choice source; the value is forwarded to whichever view model is currently attached.
		/// </summary>
		public Func<DirectoryColumnKind, IReadOnlyList<DirectoryFilterChoice>>? ChoiceProvider
		{
			get => choiceProvider;
			set
			{
				choiceProvider = value;
				viewModel.ChoiceProvider = value;
			}
		}

		/// <summary>
		/// Focuses the editor of the active mode; the shell calls this when it shows or focuses the bar.
		/// </summary>
		public void FocusActiveEditor()
		{
			if (viewModel.Mode is FilterMode.Basic)
				KeywordBox.Focus(FocusState.Programmatic);
			else if (viewModel.ShowNameCondition)
				NameEditor.FocusFirstInput();
			else
				AddConditionButton.Focus(FocusState.Programmatic);
		}

		private void FilterBar_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			// Enter confirms a text filter edit; the shell re-runs the projection immediately.
			if (e.Handled || e.Key is not VirtualKey.Enter || e.OriginalSource is not TextBox)
				return;

			e.Handled = true;
			viewModel.ApplyNow();
		}

		private void FilterBar_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			// The TextBox can consume Enter before the bubbling handler sees it.
			if (e.Handled || e.Key is not VirtualKey.Enter)
				return;

			var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject ?? e.OriginalSource as DependencyObject;
			if (!IsFilterTextInput(focused))
				return;

			e.Handled = true;
			viewModel.ApplyNow();
		}

		private static bool IsFilterTextInput(DependencyObject? element)
		{
			if (element is null)
				return false;

			// A ComboBox (for example the recursion depth) also contains a text input internally;
			// Enter there must keep the normal selection behavior.
			if (DependencyObjectHelpers.FindParent<ComboBox>(element) is not null)
				return false;

			return element is TextBox || DependencyObjectHelpers.FindParent<TextBox>(element) is not null;
		}

		private static void OnResultCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((DirectoryFilterBar)d).UpdateCount();

		private void AttachViewModel(DirectoryFilterBarViewModel value)
		{
			viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			viewModel = value;
			viewModel.PropertyChanged += ViewModel_PropertyChanged;

			// Do not clear a provider the shell already set on the view model itself.
			if (choiceProvider is not null)
				viewModel.ChoiceProvider = choiceProvider;

			OnPropertyChanged(nameof(ViewModel));
			SyncModeSelector();
			SyncDepth();
			UpdateCount();
		}

		private void DirectoryFilterBar_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
		{
			if (args.NewValue is DirectoryFilterBarViewModel filterBar && !ReferenceEquals(filterBar, viewModel))
				AttachViewModel(filterBar);
		}

		private void DirectoryFilterBar_Loaded(object sender, RoutedEventArgs e)
		{
			// SelectorBar can ignore a selection made before the control is loaded.
			SyncModeSelector();
			SyncDepth();
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(DirectoryFilterBarViewModel.Mode):
					SyncModeSelector();
					break;

				case nameof(DirectoryFilterBarViewModel.RecursiveDepth):
					SyncDepth();
					break;
			}
		}

		private void SyncModeSelector()
		{
			isSyncingMode = true;

			try
			{
				var isAdvanced = viewModel.Mode is FilterMode.Advanced;
				var item = isAdvanced ? AdvancedModeItem : BasicModeItem;

				ModeSelector.SelectedItem = item;
				BasicModeItem.IsSelected = !isAdvanced;
				AdvancedModeItem.IsSelected = isAdvanced;
			}
			finally
			{
				isSyncingMode = false;
			}
		}

		private void SyncDepth()
		{
			isSyncingDepth = true;

			try
			{
				BuildDepthItems();

				DepthCombo.SelectedItem = DepthCombo.Items
					.OfType<ComboBoxItem>()
					.FirstOrDefault(item => item.Tag is int depth && depth == viewModel.RecursiveDepth);
			}
			finally
			{
				isSyncingDepth = false;
			}
		}

		private void BuildDepthItems()
		{
			DepthCombo.Items.Clear();

			var depths = DepthPresets.ToList();

			if (!depths.Contains(viewModel.RecursiveDepth))
			{
				depths.Add(viewModel.RecursiveDepth);
				depths.Sort();
			}

			foreach (var depth in depths)
			{
				DepthCombo.Items.Add(new ComboBoxItem
				{
					Content = FormatDepth(depth),
					Tag = depth,
				});
			}
		}

		private static string FormatDepth(int depth)
			=> depth >= 64
				? Strings.FilterRecursiveDepthUnlimited.GetLocalizedResource()
				: string.Format(Strings.FilterRecursiveDepthFormat.GetLocalizedResource(), depth);

		private void UpdateCount()
		{
			if (CountText is null)
				return;

			if (ResultCount < 0)
			{
				CountText.Text = string.Empty;
				CountText.Visibility = Visibility.Collapsed;
				return;
			}

			CountText.Text = string.Format(Strings.FilterConditionCountFormat.GetLocalizedResource(), ResultCount);
			CountText.Visibility = Visibility.Visible;
		}

		private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
		{
			if (isSyncingMode)
				return;

			viewModel.Mode = sender.SelectedItem == AdvancedModeItem ? FilterMode.Advanced : FilterMode.Basic;
		}

		private void DepthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (isSyncingDepth || DepthCombo.SelectedItem is not ComboBoxItem { Tag: int depth })
				return;

			viewModel.RecursiveDepth = depth;
		}

		private void AddCondition_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not MenuFlyoutItem { Tag: string tag } || !Enum.TryParse<DirectoryColumnKind>(tag, out var column))
				return;

			viewModel.AddCondition(column, DirectoryConditionJoin.And);
		}

		private void ConditionEditor_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is not DirectoryFilterEditor { DataContext: DirectoryFilterViewModel condition } editor
				|| !focusLastAddedCondition
				|| !ReferenceEquals(condition, viewModel.Conditions.LastOrDefault()))
			{
				return;
			}

			focusLastAddedCondition = false;
			editor.FocusFirstInput();
		}

		private void JoinButton_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is Button { DataContext: DirectoryFilterViewModel condition } button)
				UpdateJoinContent(button, condition);
		}

		private void JoinButton_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not Button { DataContext: DirectoryFilterViewModel condition } button)
				return;

			condition.Join = condition.Join is DirectoryConditionJoin.And
				? DirectoryConditionJoin.Or
				: DirectoryConditionJoin.And;

			UpdateJoinContent(button, condition);
		}

		private static void UpdateJoinContent(Button button, DirectoryFilterViewModel condition)
			=> button.Content = condition.Join is DirectoryConditionJoin.And
				? Strings.FilterJoinAndContent.GetLocalizedResource()
				: Strings.FilterJoinOrContent.GetLocalizedResource();

		private void RemoveCondition_Click(object sender, RoutedEventArgs e)
		{
			if (sender is Button { DataContext: DirectoryFilterViewModel condition })
				viewModel.RemoveCondition(condition);
		}

		private void ClearAll_Click(object sender, RoutedEventArgs e)
			=> viewModel.ClearAll();

		private void FocusFilter_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
		{
			// Let the existing filter-header hotkey run while the bar is hidden.
			if (Visibility is not Visibility.Visible)
				return;

			args.Handled = true;
			FocusActiveEditor();
		}

		private void OnPropertyChanged(string? propertyName)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
