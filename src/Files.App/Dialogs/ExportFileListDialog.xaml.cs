// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Files.App.Dialogs
{
	public sealed partial class ExportFileListDialog : ContentDialog
	{
		private FrameworkElement RootAppElement
		{
			[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
			get => (FrameworkElement)MainWindow.Instance.Content;
		}

		public ExportFileListDialogViewModel? ViewModel
		{
			get => DataContext as ExportFileListDialogViewModel;
			set => DataContext = value;
		}

		public ExportFileListDialog()
		{
			InitializeComponent();
			Loaded += ExportFileListDialog_Loaded;
		}

		private void ExportFileListDialog_Loaded(object sender, RoutedEventArgs e)
		{
			(ViewModel?.Format == Files.App.Utils.Storage.FileListExportFormat.Excel
				? ExcelFormatRadioButton
				: CsvFormatRadioButton).IsChecked = true;
		}

		private void CsvFormatRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			if (ViewModel is not null)
				ViewModel.Format = Files.App.Utils.Storage.FileListExportFormat.Csv;
		}

		private void ExcelFormatRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			if (ViewModel is not null)
				ViewModel.Format = Files.App.Utils.Storage.FileListExportFormat.Excel;
		}
	}
}
