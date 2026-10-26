// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Files.App.ViewModels.UserControls
{
	public sealed record QueryExample(string Name, string Sql)
	{
		public override string ToString() => Name;
	}

	public sealed record QueryTableRow(IReadOnlyList<string> Cells);

	/// <summary>
	/// Runs read-only SQL against a snapshot of the current directory and projects mapped file results
	/// into the main file list while keeping the raw table available.
	/// </summary>
	public sealed partial class SqlQueryWorkbenchViewModel : ObservableObject
	{
		private readonly ShellViewModel shellViewModel;
		private readonly SqliteDirectoryQueryService queryService = new();

		private CancellationTokenSource? executionCancellation;
		private IReadOnlyList<ListedItem>? snapshotItems;
		private DirectoryQuerySnapshot? activeSnapshot;

		[ObservableProperty]
		private string sql = string.Empty;

		[ObservableProperty]
		private bool isBusy;

		[ObservableProperty]
		private string? errorMessage;

		[ObservableProperty]
		private string? statusMessage;

		[ObservableProperty]
		private string? elapsedText;

		[ObservableProperty]
		private bool isTruncated;

		[ObservableProperty]
		private bool hasFileResult;

		[ObservableProperty]
		private bool isSnapshotStale;

		public SqlQueryWorkbenchViewModel(ShellViewModel shellViewModel)
		{
			this.shellViewModel = shellViewModel;

			Examples =
			[
				new QueryExample(
					"Files by name",
					"SELECT file_id, name, display_name, size_bytes, modified_utc\nFROM files\nORDER BY name;"),
				new QueryExample(
					"Files by extension",
					"SELECT COALESCE(extension, '(none)') AS extension, COUNT(*) AS count, SUM(size_bytes) AS bytes\nFROM files\nGROUP BY extension\nORDER BY count DESC;"),
				new QueryExample(
					"Largest files",
					"SELECT file_id, name, size_bytes\nFROM files\nWHERE size_bytes IS NOT NULL\nORDER BY size_bytes DESC\nLIMIT 20;"),
				new QueryExample(
					"Tagged files",
					"SELECT f.file_id, f.name, t.name AS tag_name\nFROM files AS f\nJOIN file_tags AS ft ON ft.file_id = f.file_id\nJOIN tags AS t ON t.tag_id = ft.tag_id\nORDER BY f.name, t.name;"),
				new QueryExample(
					"Duplicate sizes",
					"SELECT size_bytes, COUNT(*) AS count\nFROM files\nWHERE size_bytes IS NOT NULL\nGROUP BY size_bytes\nHAVING COUNT(*) > 1\nORDER BY count DESC;"),
			];

			Sql = Examples[0].Sql;
		}

		public IReadOnlyList<QueryExample> Examples { get; }

		public ObservableCollection<string> Columns { get; } = [];

		public ObservableCollection<QueryTableRow> Rows { get; } = [];

		public async Task ExecuteAsync()
		{
			if (IsBusy)
				return;

			executionCancellation?.Dispose();
			executionCancellation = new CancellationTokenSource();
			var token = executionCancellation.Token;

			IsBusy = true;
			ErrorMessage = null;
			IsTruncated = false;
			StatusMessage = Strings.SqlRunning.GetLocalizedResource();

			try
			{
				var items = shellViewModel.GetLoadedItemsSnapshot();
				snapshotItems = items;
				activeSnapshot = shellViewModel.CreateQuerySnapshot(items);

				var result = await queryService.ExecuteAsync(activeSnapshot, Sql, null, token);
				ApplyResult(result);
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
				StatusMessage = null;
			}
			finally
			{
				IsBusy = false;
			}
		}

		public void Cancel()
			=> executionCancellation?.Cancel();

		public void ClearResult()
		{
			executionCancellation?.Cancel();

			Columns.Clear();
			Rows.Clear();
			HasFileResult = false;
			IsTruncated = false;
			IsSnapshotStale = false;
			ErrorMessage = null;
			StatusMessage = null;
			ElapsedText = null;
			activeSnapshot = null;
			snapshotItems = null;

			shellViewModel.ClearQueryView();
		}

		public void RefreshSnapshotState()
			=> IsSnapshotStale = activeSnapshot is not null
				&& activeSnapshot.SourceRevision != shellViewModel.CurrentSourceRevision;

		private void ApplyResult(DirectoryQueryResult result)
		{
			ElapsedText = string.Format(
				CultureInfo.CurrentCulture,
				Strings.SqlElapsedFormat.GetLocalizedResource(),
				result.Elapsed.TotalMilliseconds);

			Columns.Clear();
			Rows.Clear();

			if (!result.IsSuccess)
			{
				ErrorMessage = MapError(result);
				StatusMessage = null;
				return;
			}

			foreach (var column in result.Columns)
				Columns.Add(column.Name);

			foreach (var row in result.Rows)
				Rows.Add(new QueryTableRow(row.Values.Select(FormatValue).ToList()));

			IsTruncated = result.IsTruncated;

			var classified = DirectoryQueryResultClassifier.Classify(result, activeSnapshot!);

			if (classified.FileResult is not { } fileResult)
			{
				HasFileResult = false;
				IsSnapshotStale = false;
				StatusMessage = string.Format(
					CultureInfo.CurrentCulture,
					Strings.SqlRowsFormat.GetLocalizedResource(),
					result.Rows.Count);
				return;
			}

			HasFileResult = true;

			if (!string.Equals(result.DirectoryKey, shellViewModel.CurrentDirectoryKey, StringComparison.OrdinalIgnoreCase))
			{
				ErrorMessage = Strings.SqlDirectoryChanged.GetLocalizedResource();
				StatusMessage = null;
				return;
			}

			var fileItems = new List<ListedItem>(fileResult.FileIds.Count);
			foreach (var fileId in fileResult.FileIds)
			{
				if (snapshotItems is not null && fileId >= 0 && fileId < snapshotItems.Count)
					fileItems.Add(snapshotItems[(int)fileId]);
			}

			shellViewModel.ApplyQueryView(fileItems, result.DirectoryKey);
			IsSnapshotStale = result.SourceRevision != shellViewModel.CurrentSourceRevision;
			StatusMessage = string.Format(
				CultureInfo.CurrentCulture,
				Strings.SqlFilesShownFormat.GetLocalizedResource(),
				fileItems.Count);
		}

		private static string MapError(DirectoryQueryResult result)
			=> result.Error switch
			{
				DirectoryQueryError.InvalidSql => Strings.SqlErrorInvalid.GetLocalizedResource(),
				DirectoryQueryError.MultipleStatements => Strings.SqlErrorMultipleStatements.GetLocalizedResource(),
				DirectoryQueryError.NotSelect => Strings.SqlErrorNotSelect.GetLocalizedResource(),
				DirectoryQueryError.ForbiddenOperation => Strings.SqlErrorForbidden.GetLocalizedResource(),
				DirectoryQueryError.Timeout => Strings.SqlErrorTimeout.GetLocalizedResource(),
				DirectoryQueryError.Cancelled => Strings.SqlErrorCancelled.GetLocalizedResource(),
				_ => string.IsNullOrEmpty(result.ErrorMessage)
					? Strings.SqlErrorSqlite.GetLocalizedResource()
					: result.ErrorMessage!,
			};

		private static string FormatValue(object? value)
			=> value switch
			{
				null => "NULL",
				byte[] bytes => $"[BLOB {bytes.Length}]",
				double doubleValue => doubleValue.ToString(CultureInfo.CurrentCulture),
				_ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
			};
	}
}
