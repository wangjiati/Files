// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Dialogs;
using Files.App.Services.DateTimeFormatter;
using Files.App.ViewModels.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Views.Settings
{
	public sealed partial class GeneralPage : Page
	{

		public GeneralPage()
		{
			InitializeComponent();
		}

		private void RemoveStartupPage(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { DataContext: PageOnStartupViewModel page })
				ViewModel.RemovePageCommand.Execute(page);
		}

		private async void AddDirectoryFormatRule_Click(object sender, RoutedEventArgs e)
			=> await ShowDirectoryFormatRuleDialogAsync(null);

		private async void EditDirectoryFormatRule_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { Tag: DateTimeFormatDirectoryRuleViewModel rule })
				await ShowDirectoryFormatRuleDialogAsync(rule.Rule);
		}

		private void DeleteDirectoryFormatRule_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { Tag: DateTimeFormatDirectoryRuleViewModel rule })
				ViewModel.DateTimeFormatSettings.DeleteDirectoryRule(rule.Rule);
		}

		private async Task ShowDirectoryFormatRuleDialogAsync(DateTimeFormatRule? rule)
		{
			var dialog = new DateTimeFormatRuleDialog(ViewModel.DateTimeFormatSettings, rule)
			{
				XamlRoot = XamlRoot,
			};

			await dialog.ShowAsync();
		}
	}
}
