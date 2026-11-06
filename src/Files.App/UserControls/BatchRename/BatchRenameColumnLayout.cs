// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;

namespace Files.App.UserControls
{
	/// <summary>
	/// Shared widths for the batch rename preview columns. Every row template and the column
	/// header bind to the same instance so dragging a header separator updates the whole list.
	/// </summary>
	public sealed partial class BatchRenameColumnLayout : ObservableObject
	{
		public const double MinNameColumnWidth = 96;

		public const double MinPositionColumnWidth = 80;

		[ObservableProperty]
		public partial double PositionWidth { get; set; } = 130;

		[ObservableProperty]
		public partial GridLength OriginalWidth { get; set; } = new(1, GridUnitType.Star);

		[ObservableProperty]
		public partial GridLength NewWidth { get; set; } = new(1, GridUnitType.Star);

		/// <summary>Restores the persisted widths, clamped to values the preview can still render.</summary>
		public void Apply(double positionWidth, double originalRatio)
		{
			PositionWidth = Math.Clamp(positionWidth, MinPositionColumnWidth, 320);

			double ratio = Math.Clamp(originalRatio, 0.2, 0.8);
			OriginalWidth = new GridLength(ratio, GridUnitType.Star);
			NewWidth = new GridLength(1 - ratio, GridUnitType.Star);
		}

		/// <summary>Gets the original column's share of the two name columns.</summary>
		public double OriginalRatio
		{
			get
			{
				double total = OriginalWidth.Value + NewWidth.Value;
				return total > 0 ? OriginalWidth.Value / total : 0.5;
			}
		}
	}
}
