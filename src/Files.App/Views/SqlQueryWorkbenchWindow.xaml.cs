// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using Windows.Graphics;

namespace Files.App.Views
{
	/// <summary>
	/// Secondary window hosting the read-only SQL workbench for the active directory.
	/// </summary>
	public sealed partial class SqlQueryWorkbenchWindow : Window
	{
		private readonly SqlQueryWorkbenchViewModel viewModel;
		private bool isInitializing = true;

		public SqlQueryWorkbenchWindow(ShellViewModel shellViewModel)
		{
			viewModel = new SqlQueryWorkbenchViewModel(shellViewModel);

			InitializeComponent();

			if (Files.App.MainWindow.Instance is { } ownerWindow)
			{
				// An owned window stays above the main window instead of being pushed behind it.
				Win32Helper.SetWindowOwner(
					new Windows.Win32.Foundation.HWND(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id)),
					new Windows.Win32.Foundation.HWND(ownerWindow.WindowHandle));
			}

			Title = Strings.SqlWorkbenchTitle.GetLocalizedResource();
			AppWindow.Resize(new SizeInt32(1100, 800));

			ExampleCombo.ItemsSource = viewModel.Examples.ToArray();
			ExampleCombo.SelectedIndex = 0;
			SqlEditor.Text = viewModel.Sql;
			TableHeaderItems.ItemsSource = viewModel.Columns;
			TableRowsItems.ItemsSource = viewModel.Rows;

			StaleText.Text = Strings.SqlSnapshotStale.GetLocalizedResource();
			TruncatedText.Text = Strings.SqlTruncated.GetLocalizedResource();

			viewModel.PropertyChanged += ViewModel_PropertyChanged;
			Activated += SqlQueryWorkbenchWindow_Activated;
			Closed += SqlQueryWorkbenchWindow_Closed;

			if (Files.App.MainWindow.Instance is { } mainWindow)
				mainWindow.Closed += MainWindow_Closed;

			isInitializing = false;
			UpdateUi();
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
			=> UpdateUi();

		private void SqlQueryWorkbenchWindow_Activated(object sender, WindowActivatedEventArgs args)
		{
			viewModel.RefreshSnapshotState();
			UpdateUi();
		}

		private void SqlQueryWorkbenchWindow_Closed(object sender, WindowEventArgs args)
		{
			viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			viewModel.Close();

			if (Files.App.MainWindow.Instance is { } mainWindow)
				mainWindow.Closed -= MainWindow_Closed;
		}

		private void MainWindow_Closed(object sender, WindowEventArgs args)
			=> Close();

		private async void Execute_Click(object sender, RoutedEventArgs e)
			=> await ExecuteAsync();

		private async Task ExecuteAsync()
		{
			viewModel.Sql = SqlEditor.Text;
			await viewModel.ExecuteAsync();
			UpdateUi();
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
			=> viewModel.Cancel();

		private void Clear_Click(object sender, RoutedEventArgs e)
		{
			viewModel.ClearResult();
			UpdateUi();
		}

		private void ExampleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (isInitializing || ExampleCombo.SelectedItem is not QueryExample example)
				return;

			SqlEditor.Text = example.Sql;
		}

		private async void ExecuteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
		{
			args.Handled = true;
			await ExecuteAsync();
		}

		private void UpdateUi()
		{
			ExecuteButton.IsEnabled = !viewModel.IsBusy;
			CancelButton.IsEnabled = viewModel.IsBusy;
			BusyRing.IsActive = viewModel.IsBusy;

			StatusText.Text = viewModel.ErrorMessage is null
				? viewModel.StatusMessage ?? string.Empty
				: string.Empty;

			ErrorText.Text = viewModel.ErrorMessage ?? string.Empty;
			ErrorText.Visibility = ToVisibility(viewModel.ErrorMessage is not null);

			ElapsedText.Text = viewModel.ElapsedText ?? string.Empty;
			StaleText.Visibility = ToVisibility(viewModel.IsSnapshotStale);
			TruncatedText.Visibility = ToVisibility(viewModel.IsTruncated);
		}

		private static Visibility ToVisibility(bool value)
			=> value ? Visibility.Visible : Visibility.Collapsed;
	}
}
