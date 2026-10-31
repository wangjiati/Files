// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Extensions;
using Files.App.Utils.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.ViewModels.Dialogs
{
	/// <summary>
	/// Options for one export run. On search result pages the filter option is locked on
	/// (the whole view is the query result and no unfiltered set exists in memory) and
	/// recursion is disabled.
	/// </summary>
	public sealed partial class ExportFileListDialogViewModel : ObservableObject
	{
		private readonly Func<string, string> localize;

		public bool IsFilterOptionEnabled { get; }

		public bool IsRecursionEnabled { get; }

		private bool applyCurrentFilter;
		public bool ApplyCurrentFilter
		{
			get => applyCurrentFilter;
			set => SetProperty(ref applyCurrentFilter, value);
		}

		private bool includeSubfolders;
		public bool IncludeSubfolders
		{
			get => includeSubfolders;
			set => SetProperty(ref includeSubfolders, value);
		}

		public IReadOnlyList<ExportFileListColumnViewModel> Columns { get; }

		private FileListExportFormat format;
		public FileListExportFormat Format
		{
			get => format;
			set => SetProperty(ref format, value);
		}

		public bool CanExport => Columns.Any(column => column.IsChecked);

		/// <summary>Explains the disabled filter checkbox (no active filter, or search result page).</summary>
		public Visibility FilterHintVisibility
			=> IsFilterOptionEnabled ? Visibility.Collapsed : Visibility.Visible;

		public ExportFileListDialogViewModel(
			bool isSearchResultsPage,
			bool hasActiveFilter,
			bool supportsRecursion,
			Func<string, string>? localize = null)
		{
			this.localize = localize ?? (key => key.GetLocalizedResource());

			IsFilterOptionEnabled = !isSearchResultsPage && hasActiveFilter;
			IsRecursionEnabled = supportsRecursion && !isSearchResultsPage;
			applyCurrentFilter = isSearchResultsPage || hasActiveFilter;

			var defaults = new HashSet<FileListExportColumn>(FileListExportColumns.DefaultSelection);
			Columns = FileListExportColumns.All
				.Select(column => new ExportFileListColumnViewModel(
					column,
					this.localize(FileListExportColumns.GetHeaderResourceKey(column)),
					defaults.Contains(column)))
				.ToList();

			foreach (var column in Columns)
				column.PropertyChanged += (_, e) =>
				{
					if (e.PropertyName == nameof(ExportFileListColumnViewModel.IsChecked))
						OnPropertyChanged(nameof(CanExport));
				};
		}
	}

	public sealed partial class ExportFileListColumnViewModel : ObservableObject
	{
		public FileListExportColumn Column { get; }

		public string Label { get; }

		private bool isChecked;
		public bool IsChecked
		{
			get => isChecked;
			set => SetProperty(ref isChecked, value);
		}

		public ExportFileListColumnViewModel(FileListExportColumn column, string label, bool isChecked)
		{
			Column = column;
			Label = label;
			this.isChecked = isChecked;
		}
	}
}
