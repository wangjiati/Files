// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Files.App.UserControls
{
	public sealed partial class LinkBadge : UserControl
	{
		public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
			nameof(Kind),
			typeof(LinkKind),
			typeof(LinkBadge),
			new PropertyMetadata(LinkKind.None, OnKindChanged));

		public static readonly DependencyProperty BadgeBrushProperty = DependencyProperty.Register(
			nameof(BadgeBrush),
			typeof(Brush),
			typeof(LinkBadge),
			new PropertyMetadata(null));

		public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
			nameof(GlyphBrush),
			typeof(Brush),
			typeof(LinkBadge),
			new PropertyMetadata(null));

		public LinkKind Kind
		{
			get => (LinkKind)GetValue(KindProperty);
			set => SetValue(KindProperty, value);
		}

		public Brush? BadgeBrush
		{
			get => (Brush?)GetValue(BadgeBrushProperty);
			set => SetValue(BadgeBrushProperty, value);
		}

		public Brush? GlyphBrush
		{
			get => (Brush?)GetValue(GlyphBrushProperty);
			set => SetValue(GlyphBrushProperty, value);
		}

		public LinkBadge()
		{
			InitializeComponent();
			UpdateVisual();
		}

		private static void OnKindChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
			=> ((LinkBadge)dependencyObject).UpdateVisual();

		private void UpdateVisual()
		{
			HideGlyphs();

			if (Kind is LinkKind.None)
			{
				Visibility = Visibility.Collapsed;
				return;
			}

			Visibility = Visibility.Visible;

			switch (Kind)
			{
				case LinkKind.ShellShortcut:
				case LinkKind.InternetShortcut:
					Style = Application.Current.Resources["LinkBadgeShortcutStyle"] as Style;
					BadgeBackground.Visibility = Visibility.Collapsed;
					ShortcutGlyph.Visibility = Visibility.Visible;
					break;

				case LinkKind.SymbolicLink:
					Style = Application.Current.Resources["LinkBadgeSymbolicStyle"] as Style;
					BadgeBackground.Visibility = Visibility.Visible;
					SymbolicGlyph.Visibility = Visibility.Visible;
					break;

				case LinkKind.Junction:
					Style = Application.Current.Resources["LinkBadgeJunctionStyle"] as Style;
					BadgeBackground.Visibility = Visibility.Visible;
					JunctionGlyph.Visibility = Visibility.Visible;
					break;

				case LinkKind.HardLink:
					Style = Application.Current.Resources["LinkBadgeHardLinkStyle"] as Style;
					BadgeBackground.Visibility = Visibility.Visible;
					HardLinkGlyph.Visibility = Visibility.Visible;
					break;

				case LinkKind.VolumeMountPoint:
					Style = Application.Current.Resources["LinkBadgeVolumeMountStyle"] as Style;
					BadgeBackground.Visibility = Visibility.Visible;
					MountGlyph.Visibility = Visibility.Visible;
					break;

				default:
					Visibility = Visibility.Collapsed;
					break;
			}
		}

		private void HideGlyphs()
		{
			ShortcutGlyph.Visibility = Visibility.Collapsed;
			SymbolicGlyph.Visibility = Visibility.Collapsed;
			JunctionGlyph.Visibility = Visibility.Collapsed;
			HardLinkGlyph.Visibility = Visibility.Collapsed;
			MountGlyph.Visibility = Visibility.Collapsed;
		}
	}
}
