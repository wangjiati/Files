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

		/// <inheritdoc/>
		public double RulePanelWidth
		{
			get => Get(320d);
			set => Set(value);
		}

		/// <inheritdoc/>
		public double PositionColumnWidth
		{
			get => Get(130d);
			set => Set(value);
		}

		/// <inheritdoc/>
		public double PreviewOriginalColumnRatio
		{
			get => Get(0.5d);
			set => Set(value);
		}
	}
}
