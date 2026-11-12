// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Settings
{
	internal sealed partial class InfoPaneSettingsService : BaseObservableJsonSettings, IInfoPaneSettingsService
	{
		// The pane clamps are shared with MainPage so per-tick resize accumulation matches persisted writes
		public const double MinPaneWidthPx = 300d;
		public const double MaxPaneWidthPx = 500d;
		public const double MinPaneHeightPx = 100d;

		public bool IsInfoPaneEnabled
		{
			get => Get(false);
			set => Set(value);
		}

		public double HorizontalSizePx
		{
			get => Math.Max(MinPaneHeightPx, Get(300d));
			set => Set(Math.Max(MinPaneHeightPx, value));
		}

		public double VerticalSizePx
		{
			get => Math.Clamp(Get(300d), MinPaneWidthPx, MaxPaneWidthPx);
			set => Set(Math.Clamp(value, MinPaneWidthPx, MaxPaneWidthPx));
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
