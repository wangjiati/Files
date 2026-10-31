// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contexts;
using Files.App.Data.Enums;
using Files.App.Extensions;
using Files.App.Utils.FileTags;
using Files.App.Utils.StatusCenter;
using Files.App.Utils.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.Pickers;

using Files.App.Utils;
using Microsoft.Extensions.Logging;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class ExportFileListAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;
		private static readonly StatusCenterViewModel StatusCenterViewModel = Ioc.Default.GetRequiredService<StatusCenterViewModel>();
		private static readonly IFileTagsSettingsService TagSettings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();

		public string Label
			=> "ExportFileList".GetLocalizedResource();

		public string Description
			=> "ExportFileListDescription".GetLocalizedResource();

		public RichGlyph Glyph
			=> new(baseGlyph: "\uE8E5");

		public ActionCategory Category
			=> ActionCategory.FileSystem;

		public bool IsExecutable
			=> context.ShellPage?.ShellViewModel is not null
			&& context.PageType is ContentPageTypes.Folder
				or ContentPageTypes.Library
				or ContentPageTypes.CloudDrive
				or ContentPageTypes.SearchResults
				or ContentPageTypes.MtpDevice
				or ContentPageTypes.Ftp;

		public ExportFileListAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public async Task ExecuteAsync(object? parameter = null)
		{
			var shellPage = context.ShellPage;
			var shellViewModel = shellPage?.ShellViewModel;
			if (shellViewModel?.WorkingDirectory is not { } workingDirectory)
				return;

			// Search and SQL query views both show a query result; the dialog locks the
			// filter on (the view IS the result) and disables recursion for them.
			var isResultView = context.PageType == ContentPageTypes.SearchResults || shellViewModel.IsQueryViewActive;
			var hasActiveFilter = isResultView || !shellViewModel.DirectoryFilter.IsEmpty;
			var supportsRecursion = !isResultView && context.PageType is ContentPageTypes.Folder or ContentPageTypes.CloudDrive;

			var dialogViewModel = new ViewModels.Dialogs.ExportFileListDialogViewModel(isResultView, hasActiveFilter, supportsRecursion);
			var dialog = new Dialogs.ExportFileListDialog { ViewModel = dialogViewModel };

			if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
				dialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

			if (await dialog.TryShowAsync() != ContentDialogResult.Primary)
				return;

			var pickedFile = await PickDestinationFileAsync(workingDirectory, dialogViewModel.Format);
			if (pickedFile?.Path is not { Length: > 0 } destinationPath)
				return;

			var folderSettings = shellPage!.InstanceViewModel.FolderSettings;
			var exportContext = new FileListExportContext();
			var rowCount = 0L;
			try
			{
				await Task.Run(() =>
				{
					var columns = dialogViewModel.Columns
						.Where(column => column.IsChecked)
						.Select(column => (column.Column, FileListExportColumns.GetHeaderResourceKey(column.Column).GetLocalizedResource()))
						.ToList();

					var rows = BuildRows(
						shellViewModel,
						workingDirectory,
						dialogViewModel,
						exportContext,
						folderSettings.DirectorySortOption,
						folderSettings.DirectorySortDirection);

					// Count while streaming; the writers never materialize the row set.
					var countedRows = rows.Select(row =>
					{
						rowCount++;
						return row;
					});

					var temporaryPath = Path.GetTempFileName();
					try
					{
						using (var stream = File.Create(temporaryPath))
						{
							if (dialogViewModel.Format == FileListExportFormat.Excel)
								FileListXlsxWriter.Write(stream, "ExportFileList_SheetName".GetLocalizedResource(), columns, countedRows);
							else
								FileListCsvWriter.Write(stream, columns, countedRows);
						}

						File.Move(temporaryPath, destinationPath, overwrite: true);
					}
					catch
					{
						try { File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
						throw;
					}
				});

				StatusCenterHelper.AddCard_ExportFileList(ReturnResult.Success, destinationPath, rowCount, exportContext.SkippedCount);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "File list export failed: {Destination}", destinationPath);
				StatusCenterHelper.AddCard_ExportFileList(ReturnResult.Failed, destinationPath, rowCount, exportContext.SkippedCount);
			}
		}

		private static IEnumerable<FileListExportRow> BuildRows(
			ViewModels.ShellViewModel shellViewModel,
			string workingDirectory,
			ViewModels.Dialogs.ExportFileListDialogViewModel dialogViewModel,
			FileListExportContext exportContext,
			SortOption sortOption,
			SortDirection sortDirection)
		{
			var items = dialogViewModel.ApplyCurrentFilter
				? shellViewModel.FilesAndFolders.ToList()
				: shellViewModel.GetFullItemListSnapshot();

			var rootRows = FileListExportEnumerator.EnumerateCurrentLevel(items, workingDirectory, item => ResolveTagNames(item.FileTags)).ToList();

			if (!dialogViewModel.IncludeSubfolders)
				return rootRows;

			var filterSpec = dialogViewModel.ApplyCurrentFilter ? shellViewModel.DirectoryFilter : null;

			return FileListExportEnumerator.EnumerateRecursive(
				rootRows,
				workingDirectory,
				sortOption,
				sortDirection,
				filterSpec,
				LoadTagsByPath(workingDirectory),
				exportContext,
				formatTagNames: ResolveTagNames);
		}

		private static string? ResolveTagNames(IReadOnlyList<string>? tagIds)
		{
			var tags = TagSettings.GetTagsByIds(tagIds as string[] ?? tagIds?.ToArray());
			return tags is { Count: > 0 } ? string.Join("; ", tags.Select(tag => tag.Name)) : null;
		}

		private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadTagsByPath(string rootPath)
		{
			try
			{
				return FileTagsHelper.GetDbInstance()
					.GetAllUnderPath(rootPath)
					.GroupBy(tagged => tagged.FilePath, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(
						group => group.Key,
						group => (IReadOnlyList<string>)group.SelectMany(tagged => tagged.Tags).Distinct().ToList(),
						StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
			{
				return new Dictionary<string, IReadOnlyList<string>>();
			}
		}

		private static async Task<StorageFile?> PickDestinationFileAsync(string workingDirectory, FileListExportFormat format)
		{
			var picker = new FileSavePicker();
			WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);

			picker.SuggestedFileName = $"{Path.GetFileName(workingDirectory.TrimEnd('\\'))}_FileList_{DateTime.Now:yyyyMMdd}";
			if (format == FileListExportFormat.Excel)
				picker.FileTypeChoices.Add("Excel", [".xlsx"]);
			else
				picker.FileTypeChoices.Add("CSV", [".csv"]);

			return await picker.PickSaveFileAsync();
		}

		public void Context_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.PageType):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
