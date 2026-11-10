// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT;

namespace Files.App.UserControls
{
	public sealed partial class InfoPane : UserControl
	{
		private readonly IInfoPaneSettingsService PaneSettingsService;

		private readonly ICommandManager Commands;

		private IContentPageContext contentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();

		public InfoPaneViewModel ViewModel { get; private set; }

		private ObservableContext Context { get; } = new();

		public InfoPane()
		{
			InitializeComponent();
			PaneSettingsService = Ioc.Default.GetRequiredService<IInfoPaneSettingsService>();
			Commands = Ioc.Default.GetRequiredService<ICommandManager>();
			ViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
		}

		public void ApplyLayout(PreviewPaneLayout layout)
		{
			// The tabs visual state collapses the details when the preview tab is selected.
			if (layout is not PreviewPaneLayout.RightTabs && ViewModel.SelectedTab != InfoPaneTabs.Details)
				ViewModel.SelectedTab = InfoPaneTabs.Details;

			switch (layout)
			{
				case PreviewPaneLayout.RightSplit:
					(MinWidth, MinHeight) = (300, 0);
					VisualStateManager.GoToState(this, "Vertical", true);
					RowSplitter.Visibility = Visibility.Visible;
					ColumnSplitter.Visibility = Visibility.Collapsed;
					RowSplitter.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth));
					RootPropertiesScrollViewer.Margin = new Thickness(0, 8, 0, 0);
					PreviewColumn.Width = new(2, GridUnitType.Star);
					PropertiesColumn.Width = new(3, GridUnitType.Star);
					var splitRatio = Math.Clamp(PaneSettingsService.SplitRatio, 0.22d, 0.72d);
					PreviewRow.Height = new(splitRatio, GridUnitType.Star);
					PropertiesRow.Height = new(1d - splitRatio, GridUnitType.Star);
					break;
				case PreviewPaneLayout.RightTabs:
					(MinWidth, MinHeight) = (300, 0);
					VisualStateManager.GoToState(this, "Vertical", true);
					RowSplitter.Visibility = Visibility.Collapsed;
					ColumnSplitter.Visibility = Visibility.Collapsed;
					RootPropertiesScrollViewer.Margin = new Thickness(0);
					PreviewColumn.Width = new(2, GridUnitType.Star);
					PropertiesColumn.Width = new(3, GridUnitType.Star);

					// The preview row keeps its share so that switching back to the details tab restores 2:3.
					PreviewRow.Height = new GridLength(2, GridUnitType.Star);
					PropertiesRow.Height = ViewModel.SelectedTab is InfoPaneTabs.Preview
						? new GridLength(0)
						: new GridLength(3, GridUnitType.Star);
					break;
				case PreviewPaneLayout.Bottom:
					(MinWidth, MinHeight) = (0, 140);
					VisualStateManager.GoToState(this, "Horizontal", true);
					ColumnSplitter.Visibility = Visibility.Visible;
					RowSplitter.Visibility = Visibility.Collapsed;
					ColumnSplitter.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast));
					RootPropertiesScrollViewer.Margin = new Thickness(8, 0, 0, 0);
					ApplyBottomDetailsWidth();
					break;
			}

			// The details/preview selector only applies when the two panes share the same area.
			TabSelectorPanel.Visibility = layout is PreviewPaneLayout.RightTabs ? Visibility.Visible : Visibility.Collapsed;

			LayoutRightSplitButton.IsChecked = layout is PreviewPaneLayout.RightSplit;
			LayoutBottomButton.IsChecked = layout is PreviewPaneLayout.Bottom;
			LayoutRightTabsButton.IsChecked = layout is PreviewPaneLayout.RightTabs;
		}

		private void RowSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			var totalHeight = PreviewRow.ActualHeight + PropertiesRow.ActualHeight;
			if (totalHeight <= 0d)
				return;

			var splitRatio = Math.Clamp(PreviewRow.ActualHeight / totalHeight, 0.22d, 0.72d);
			PaneSettingsService.SplitRatio = splitRatio;

			PreviewRow.Height = new GridLength(splitRatio, GridUnitType.Star);
			PropertiesRow.Height = new GridLength(1d - splitRatio, GridUnitType.Star);
		}

		private void ColumnSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			var detailsWidth = ClampBottomDetailsWidth(PropertiesColumn.ActualWidth);
			PaneSettingsService.BottomDetailsWidthPx = detailsWidth;

			PropertiesColumn.Width = new GridLength(detailsWidth);
		}

		private void ApplyBottomDetailsWidth()
			=> PropertiesColumn.Width = new GridLength(ClampBottomDetailsWidth(PaneSettingsService.BottomDetailsWidthPx));

		private double ClampBottomDetailsWidth(double detailsWidth)
		{
			// Keep at least 160 px available for the preview column.
			var maxWidth = Math.Max(132d, Root.ActualWidth - 160d);

			return Math.Clamp(detailsWidth, 132d, maxWidth);
		}

		private void LayoutButton_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<PreviewPaneLayout>(tag, out var layout))
				return;

			// Re-selecting the current layout doesn't raise a settings change event, so apply it here as well.
			PaneSettingsService.Layout = layout;
			ApplyLayout(layout);
		}

		private void Root_Unloaded(object sender, RoutedEventArgs e)
		{
			ViewModel.UnloadPreview();
			PreviewControlPresenter.Content = null;
			Bindings.StopTracking();
		}

		private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			Context.IsHorizontal = Root.ActualWidth >= Root.ActualHeight;

			if (PaneSettingsService.Layout is PreviewPaneLayout.Bottom)
				ApplyBottomDetailsWidth();
		}

		private void MenuFlyoutItem_Tapped(object sender, TappedRoutedEventArgs e)
			=> ViewModel?.UpdateSelectedItemPreviewAsync(true);

		[DynamicWindowsRuntimeCast(typeof(UserControl))]
		private void FileTag_PointerEntered(object sender, PointerRoutedEventArgs e)
		{
			VisualStateManager.GoToState((UserControl)sender, "PointerOver", true);
		}

		[DynamicWindowsRuntimeCast(typeof(UserControl))]
		private void FileTag_PointerExited(object sender, PointerRoutedEventArgs e)
		{
			VisualStateManager.GoToState((UserControl)sender, "Normal", true);
		}

		private sealed partial class ObservableContext : ObservableObject
		{
			private bool isHorizontal = false;
			public bool IsHorizontal
			{
				get => isHorizontal;
				set => SetProperty(ref isHorizontal, value);
			}
		}

		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		[DynamicWindowsRuntimeCast(typeof(StackPanel))]
		private void TagItem_Tapped(object sender, TappedRoutedEventArgs e)
		{
			var tagName = ((sender as StackPanel)?.Children[1] as TextBlock)?.Text;
			if (tagName is null)
				return;

			contentPageContext.ShellPage?.SubmitSearch(FolderSearch.FormatTagQuery(tagName));
		}
	}
}