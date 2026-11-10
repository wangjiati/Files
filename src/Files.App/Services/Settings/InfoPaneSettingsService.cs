// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Settings
{
	internal sealed partial class InfoPaneSettingsService : BaseObservableJsonSettings, IInfoPaneSettingsService
	{
		public bool IsInfoPaneEnabled
		{
			get => Get(false);
			set => Set(value);
		}

		public double HorizontalSizePx
		{
			get => Math.Max(100d, Get(300d));
			set => Set(Math.Max(100d, value));
		}

		public double VerticalSizePx
		{
			get => Math.Clamp(Get(300d), 300d, 500d);
			set => Set(Math.Clamp(value, 300d, 500d));
		}

		public PreviewPaneLayout Layout
		{
			get => Get(PreviewPaneLayout.RightSplit);
			set => Set(value);
		}

		public double SplitRatio
		{
			get => Math.Clamp(Get(0.42d), 0.22d, 0.72d);
			set => Set(Math.Clamp(value, 0.22d, 0.72d));
		}

		public double BottomDetailsWidthPx
		{
			get => Math.Max(132d, Get(300d));
			set => Set(Math.Max(132d, value));
		}

		public double MediaVolume
		{
			get => Math.Min(Math.Max(Get(1d), 0d), 1d);
			set => Set(Math.Max(0d, Math.Min(value, 1d)));
		}

		public InfoPaneTabs SelectedTab
		{
			get => Get(InfoPaneTabs.Details);
			set => Set(value);
		}

		public InfoPaneSettingsService(ISettingsSharingContext settingsSharingContext)
		{
			RegisterSettingsContext(settingsSharingContext);
		}

		protected override void RaiseOnSettingChangedEvent(object sender, SettingChangedEventArgs e)
		{
			base.RaiseOnSettingChangedEvent(sender, e);
		}
	}
}
