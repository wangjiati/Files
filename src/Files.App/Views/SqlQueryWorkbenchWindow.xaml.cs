// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.UserControls;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SQL.Formatter;
using SQL.Formatter.Core;
using SQL.Formatter.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;

namespace Files.App.Views
{
	/// <summary>
	/// Secondary window hosting the read-only SQL workbench for the active directory.
	/// </summary>
	public sealed partial class SqlQueryWorkbenchWindow : Window
	{
		private readonly SqlQueryWorkbenchViewModel viewModel;
		private readonly AppSystemBackdrop backdrop;
		private bool isInitializing = true;
		private bool isClosed;
		private bool isHighlighting;
		private int highlightVersion;
		private string? lastHighlightedText;
		private SqlEditorHighlighter? highlighter;

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

			var windowScale = App.AppModel.AppWindowDPI;
			AppWindow.Resize(new SizeInt32(
				Math.Max(1, Convert.ToInt32(1100 * windowScale)),
				Math.Max(1, Convert.ToInt32(800 * windowScale))));

			backdrop = new AppSystemBackdrop(isSecondaryWindow: true);
			SystemBackdrop = backdrop;

			ExampleList.ItemsSource = viewModel.Examples;
			ExampleList.SelectedIndex = 0;
			SqlEditor.Document.SetText(TextSetOptions.None, viewModel.Sql);
			TableHeaderItems.ItemsSource = viewModel.Columns;
			TableRowsItems.ItemsSource = viewModel.Rows;

			viewModel.PropertyChanged += ViewModel_PropertyChanged;
			Activated += SqlQueryWorkbenchWindow_Activated;
			Closed += SqlQueryWorkbenchWindow_Closed;
			SqlEditor.Loaded += SqlEditor_Loaded;
			SqlEditor.ActualThemeChanged += SqlEditor_ActualThemeChanged;

			if (Files.App.MainWindow.Instance is { } mainWindow)
				mainWindow.Closed += MainWindow_Closed;

			isInitializing = false;
			UpdateUi();
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
			=> UpdateUi();

		private void SqlQueryWorkbenchWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
		{
			backdrop.SetInputActive(args.WindowActivationState is not WindowActivationState.Deactivated);

			viewModel.RefreshSnapshotState();
			UpdateUi();

			if (args.WindowActivationState is not WindowActivationState.Deactivated)
				SqlEditor.Focus(FocusState.Programmatic);
		}

		private void SqlQueryWorkbenchWindow_Closed(object sender, WindowEventArgs args)
		{
			isClosed = true;
			viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			SqlEditor.Loaded -= SqlEditor_Loaded;
			SqlEditor.ActualThemeChanged -= SqlEditor_ActualThemeChanged;
			viewModel.Close();

			if (Files.App.MainWindow.Instance is { } mainWindow)
				mainWindow.Closed -= MainWindow_Closed;
		}

		private void MainWindow_Closed(object sender, WindowEventArgs args)
			=> Close();

		private void SqlEditor_Loaded(object sender, RoutedEventArgs e)
		{
			highlighter = new SqlEditorHighlighter(SqlEditor, SqlEditor.ActualTheme == ElementTheme.Dark);
			HighlightNow();
		}

		private void SqlEditor_ActualThemeChanged(FrameworkElement sender, object args)
		{
			highlighter = new SqlEditorHighlighter(SqlEditor, sender.ActualTheme == ElementTheme.Dark);
			lastHighlightedText = null;
			HighlightNow();
		}

		private async void SqlEditor_TextChanged(object sender, RoutedEventArgs e)
		{
			if (isInitializing || isClosed || isHighlighting || highlighter is null)
				return;

			SqlEditor.Document.GetText(TextGetOptions.None, out var text);
			if (string.Equals(text, lastHighlightedText, StringComparison.Ordinal))
				return;

			var version = ++highlightVersion;
			await Task.Delay(120);
			if (version != highlightVersion || isClosed || isHighlighting)
				return;

			HighlightNow();
		}

		private void HighlightNow()
		{
			if (isClosed || highlighter is null || isHighlighting)
				return;

			SqlEditor.Document.GetText(TextGetOptions.None, out var text);

			isHighlighting = true;
			try
			{
				highlighter.Highlight(text);
				lastHighlightedText = text;
			}
			finally
			{
				isHighlighting = false;
			}
		}

		private string GetEditorText()
		{
			SqlEditor.Document.GetText(TextGetOptions.None, out var text);
			return text.TrimEnd('\r', '\n');
		}

		private void SetEditorText(string text)
		{
			SqlEditor.Document.SetText(TextSetOptions.None, text);
			HighlightNow();
		}

		private async void Execute_Click(object sender, RoutedEventArgs e)
			=> await ExecuteAsync();

		private async Task ExecuteAsync()
		{
			FormatErrorText.Visibility = Visibility.Collapsed;
			viewModel.Sql = GetEditorText();
			await viewModel.ExecuteAsync();
			if (isClosed)
				return;

			UpdateUi();
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
			=> viewModel.Cancel();

		private void Clear_Click(object sender, RoutedEventArgs e)
		{
			FormatErrorText.Visibility = Visibility.Collapsed;
			viewModel.ClearResult();
			UpdateUi();
		}

		private async void Beautify_Click(object sender, RoutedEventArgs e)
		{
			FormatErrorText.Visibility = Visibility.Collapsed;

			var sql = GetEditorText();
			if (string.IsNullOrWhiteSpace(sql))
				return;

			BeautifyButton.IsEnabled = false;
			try
			{
				var formatted = await Task.Run(() => SqlFormatter
					.Of(Dialect.StandardSql)
					.Format(sql, FormatConfig.Builder()
						.Indent("    ")
						.Case(CaseTypes.NONE)
						.Build()));

				if (isClosed)
					return;

				if (!string.IsNullOrWhiteSpace(formatted))
				{
					SetEditorText(formatted);
					viewModel.Sql = formatted;
				}
			}
			catch (Exception)
			{
				if (isClosed)
					return;

				FormatErrorText.Text = Strings.SqlFormatFailed.GetLocalizedResource();
				FormatErrorText.Visibility = Visibility.Visible;
			}
			finally
			{
				if (!isClosed)
					BeautifyButton.IsEnabled = !viewModel.IsBusy;
			}
		}

		private void ExampleList_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (e.ClickedItem is not QueryExample example)
				return;

			FormatErrorText.Visibility = Visibility.Collapsed;
			SetEditorText(example.Sql);
			viewModel.Sql = example.Sql;
			SqlEditor.Focus(FocusState.Programmatic);
		}

		private async void SqlEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key is not VirtualKey.Enter)
				return;

			var controlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
			if ((controlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0)
				return;

			e.Handled = true;
			await ExecuteAsync();
		}

		private void UpdateUi()
		{
			if (isInitializing || isClosed)
				return;

			ExecuteButton.IsEnabled = !viewModel.IsBusy;
			CancelButton.IsEnabled = viewModel.IsBusy;
			BeautifyButton.IsEnabled = !viewModel.IsBusy;
			BusyRing.IsActive = viewModel.IsBusy;

			StatusText.Text = viewModel.ErrorMessage is null
				? viewModel.StatusMessage ?? string.Empty
				: string.Empty;

			ElapsedText.Text = viewModel.ElapsedText ?? string.Empty;
			DirectoryText.Text = string.IsNullOrEmpty(viewModel.DirectoryPath)
				? string.Empty
				: string.Format(
					CultureInfo.CurrentCulture,
					Strings.SqlDirectoryFormat.GetLocalizedResource(),
					viewModel.DirectoryPath);

			ErrorBar.Message = viewModel.ErrorMessage ?? string.Empty;
			ErrorBar.IsOpen = viewModel.ErrorMessage is not null;

			var notice = BuildNoticeMessage();
			NoticeBar.Message = notice;
			NoticeBar.IsOpen = notice.Length > 0;

			ClearButton.IsEnabled =
				viewModel.Columns.Count > 0
				|| viewModel.ErrorMessage is not null
				|| viewModel.IsTruncated
				|| viewModel.IsSnapshotStale;

			EmptyResultsText.Visibility = ToVisibility(
				viewModel.Columns.Count == 0
				&& viewModel.ErrorMessage is null
				&& !viewModel.IsBusy);
		}

		private string BuildNoticeMessage()
		{
			var messages = new List<string>(2);
			if (viewModel.IsSnapshotStale)
				messages.Add(Strings.SqlSnapshotStale.GetLocalizedResource());
			if (viewModel.IsTruncated)
				messages.Add(Strings.SqlTruncated.GetLocalizedResource());

			return string.Join(Environment.NewLine, messages);
		}

		private static Visibility ToVisibility(bool value)
			=> value ? Visibility.Visible : Visibility.Collapsed;
	}
}
