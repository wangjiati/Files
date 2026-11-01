// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Contracts
{
	public interface IBatchRenameSettingsService : IBaseSettingsService, INotifyPropertyChanged
	{
		/// <summary>
		/// Gets or sets a value indicating whether the rules panel docks to the left of the batch rename overlay.
		/// </summary>
		bool RulePanelDockedLeft { get; set; }

		/// <summary>
		/// Gets or sets the name of the batch rename preset that was applied last.
		/// </summary>
		string LastPresetName { get; set; }
	}
}
