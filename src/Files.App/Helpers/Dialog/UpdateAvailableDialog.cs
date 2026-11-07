// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Dialogs;
using Files.App.Services.Update;
using Files.App.ViewModels.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Files.App.Helpers;

internal static class UpdateAvailableDialog
{
	public static async Task ShowAsync(IUpdateService updateService)
	{
		if (updateService.AvailableVersion is not { } version)
			return;

		var applicationSettings = Ioc.Default.GetRequiredService<IApplicationSettingsService>();

		var viewModel = new DynamicDialogViewModel
		{
			TitleText = string.Format(Strings.UpdateAvailableTitle.GetLocalizedResource(), version),
			SubtitleText = string.Format(
				Strings.UpdateAvailableSubtitle.GetLocalizedResource(),
				version.Major,
				version.Minor,
				version.Build,
				version.Revision),
			DisplayControl = new ScrollViewer
			{
				MaxHeight = 320,
				Content = new TextBlock
				{
					Text = UpdateNotesFormatter.ToPreview(updateService.AvailableReleaseNotes),
					TextWrapping = TextWrapping.Wrap,
				},
			},
			PrimaryButtonText = Strings.DownloadUpdate.GetLocalizedResource(),
			SecondaryButtonText = Strings.RemindMeLater.GetLocalizedResource(),
			DynamicButtons = DynamicDialogButtons.Primary | DynamicDialogButtons.Secondary,
			PrimaryButtonAction = (viewModel, _) =>
			{
				OpenReleasePage(updateService.ReleasePageUrl);
				viewModel.Hide();
			},
			SecondaryButtonAction = (viewModel, _) =>
			{
				applicationSettings.SkippedVersion = version.ToString();
				viewModel.Hide();
			},
		};

		using var dialog = new DynamicDialog(viewModel);
		await dialog.ShowAsync();
	}

	private static void OpenReleasePage(Uri? uri)
	{
		if (uri is { Scheme: "https" } && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
			_ = Launcher.LaunchUriAsync(uri);
	}
}
