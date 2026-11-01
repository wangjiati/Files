// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Settings
{
	internal sealed partial class BatchRenameSettingsService : BaseObservableJsonSettings, IBatchRenameSettingsService
	{
		public BatchRenameSettingsService(ISettingsSharingContext settingsSharingContext)
		{
			// Register root
			RegisterSettingsContext(settingsSharingContext);
		}

		/// <inheritdoc/>
		public bool RulePanelDockedLeft
		{
			get => Get(false);
			set => Set(value);
		}

		/// <inheritdoc/>
		public string LastPresetName
		{
			get => Get(string.Empty) ?? string.Empty;
			set => Set(value);
		}
	}
}
