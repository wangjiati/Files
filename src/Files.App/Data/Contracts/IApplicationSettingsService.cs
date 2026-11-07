// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Contracts
{
	public interface IApplicationSettingsService : IBaseSettingsService
	{
		/// <summary>
		/// Gets or sets a value indicating whether or not the user clicked the 'review' prompt.
		/// </summary>
		bool HasClickedReviewPrompt { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether or not the user clicked the 'sponsor' prompt.
		/// </summary>
		bool HasClickedSponsorPrompt { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether or not to display a prompt when running the app as administrator.
		/// </summary>
		bool ShowRunningAsAdminPrompt { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether or not to display a prompt when creating an alternate data stream.
		/// </summary>
		bool ShowDataStreamsAreHiddenPrompt { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether or not to check for updates at startup.
		/// </summary>
		bool CheckForUpdates { get; set; }

		/// <summary>
		/// Gets or sets the version the user chose not to be reminded about.
		/// </summary>
		string? SkippedVersion { get; set; }

	}
}
