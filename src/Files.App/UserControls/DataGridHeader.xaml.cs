// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace Files.App.UserControls
{
	public sealed partial class DataGridHeader : UserControl
	{
		[GeneratedDependencyProperty]
		public partial string? Header { get; set; }

		[GeneratedDependencyProperty(DefaultValue = true)]
		public partial bool CanBeSorted { get; set; }

		[GeneratedDependencyProperty]
		public partial SortDirection? ColumnSortOption { get; set; }

		[GeneratedDependencyProperty]
		public partial bool CanBeFiltered { get; set; }

		[GeneratedDependencyProperty]
		public partial bool IsFilterActive { get; set; }

		public ICommand? Command { get; set; }
		public object? CommandParameter { get; set; }

		public ICommand? FilterCommand { get; set; }
		public object? FilterCommandParameter { get; set; }

		partial void OnCanBeFilteredChanged(bool newValue)
		{
			if (FilterButton is not null)
				FilterButton.Visibility = newValue ? Visibility.Visible : Visibility.Collapsed;
		}

		partial void OnIsFilterActiveChanged(bool newValue)
		{
			if (FilterActiveDot is not null)
				FilterActiveDot.Visibility = newValue ? Visibility.Visible : Visibility.Collapsed;
		}

		partial void OnColumnSortOptionChanged(SortDirection? newValue)
		{
			VisualStateManager.GoToState(
				this,
				newValue switch
				{
					SortDirection.Ascending => "SortAscending",
					SortDirection.Descending => "SortDescending",
					_ => "Unsorted",
				},
				true);
		}

		public DataGridHeader()
		{
			InitializeComponent();
		}
	}
}
