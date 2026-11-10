// Copyright (c) Files Community
// Licensed under the MIT License.

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
				case PreviewPaneLayout.RightTabs:
					(MinWidth, MinHeight) = (300, 0);
					VisualStateManager.GoToState(this, "Vertical", true);
					break;
				case PreviewPaneLayout.Bottom:
					(MinWidth, MinHeight) = (0, 140);
					VisualStateManager.GoToState(this, "Horizontal", true);
					break;
			}

			// The details/preview selector only applies when the two panes share the same area.
			TabSelectorPanel.Visibility = layout is PreviewPaneLayout.RightTabs ? Visibility.Visible : Visibility.Collapsed;

			LayoutRightSplitButton.IsChecked = layout is PreviewPaneLayout.RightSplit;
			LayoutBottomButton.IsChecked = layout is PreviewPaneLayout.Bottom;
			LayoutRightTabsButton.IsChecked = layout is PreviewPaneLayout.RightTabs;
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
			=> Context.IsHorizontal = Root.ActualWidth >= Root.ActualHeight;

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