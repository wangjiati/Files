// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Files.App.ViewModels.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Dialogs
{
	/// <summary>
	/// Edits a folder scoped date time format rule.
	/// </summary>
	public sealed partial class DateTimeFormatRuleDialog : ContentDialog
	{
		public DateTimeFormatRuleDialogViewModel ViewModel { get; }

		public DateTimeFormatRuleDialog(
			DateTimeFormatSettingsViewModel settings,
			DateTimeFormatRule? existingRule = null,
			string? initialDirectoryPath = null,
			DateTimeColumnKind? initialColumn = null)
		{
			ViewModel = new(settings, existingRule, initialDirectoryPath, initialColumn);

			InitializeComponent();
		}

		private void BrowseFolder_Click(object sender, RoutedEventArgs e)
		{
			var commonDialogService = Ioc.Default.GetRequiredService<ICommonDialogService>();

			if (commonDialogService.Open_FileOpenDialog(
				MainWindow.Instance.WindowHandle,
				true,
				[],
				Environment.SpecialFolder.Desktop,
				out var folderPath))
			{
				ViewModel.DirectoryPath = folderPath;
			}
		}

		private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			args.Cancel = !ViewModel.Save();
		}

		private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			ViewModel.Delete();
		}
	}
}
