// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Windows.Storage;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Builds the executable rename plan from preview rows and runs it against the file system.
	/// </summary>
	/// <remarks>
	/// Plan construction is a pure function of the rows. Rows that will not be renamed are
	/// dropped: unchanged rows, unchecked rows, skipped rows and error rows, plus rows whose
	/// final name equals their current name. Chained renames — where one row's destination is
	/// another row's source — are ordered so the destination is vacated first, which also makes
	/// the reverse of the plan a safe undo replay order. A swap cycle (a→b, b→a) has no such
	/// order; it is emitted in input order and execution reports the failure.
	/// </remarks>
	public sealed class RenameExecutor : IRenameExecutor
	{
		/// <summary>
		/// Builds the execution plan for the given rows in list order.
		/// </summary>
		/// <param name="rows">The preview rows to plan from.</param>
		/// <param name="strategy">
		/// Kept for callers building a plan alongside an execute call; the rows' conflict
		/// decisions were already resolved under this strategy, so it does not affect the plan.
		/// </param>
		public static IReadOnlyList<RenameExecutionItem> BuildExecutionPlan(IReadOnlyList<RenamePreviewRow> rows, ConflictStrategy strategy)
		{
			ArgumentNullException.ThrowIfNull(rows);

			List<RenamePreviewRow> pending = new(rows.Count);
			foreach (RenamePreviewRow row in rows)
			{
				if (IsExecutable(row))
					pending.Add(row);
			}

			List<RenameExecutionItem> plan = new(pending.Count);
			while (pending.Count > 0)
			{
				bool progressed = false;
				for (int i = 0; i < pending.Count;)
				{
					if (VacatesAnotherRow(pending, i))
					{
						i++;
						continue;
					}

					plan.Add(ToItem(pending[i]));
					pending.RemoveAt(i);
					progressed = true;
				}

				// A cycle blocks every remaining row; emit them as-is in input order.
				if (!progressed)
				{
					foreach (RenamePreviewRow row in pending)
						plan.Add(ToItem(row));
					pending.Clear();
				}
			}

			return plan;
		}

		/// <summary>
		/// Executes every plan item in order, one rename at a time, and registers the successful
		/// renames as a single undoable <see cref="StorageHistory"/>.
		/// </summary>
		/// <remarks>
		/// Every rename goes through <see cref="IFilesystemHelpers.RenameAsync(IStorageItemWithPath, string, NameCollisionOption, bool, bool)"/>
		/// with <see cref="NameCollisionOption.FailIfExists"/> and per-item history disabled, so the batch is
		/// registered as one merged history entry instead. That entry point is the same one inline rename
		/// uses, and it already routes a case-only new name — equal to the current name ignoring case —
		/// to a replace-collision rename rather than failing with "already exists". An overwrite item first
		/// sends its destination to the recycle bin; a destination vacated by an earlier item of the same
		/// plan (a chained rename) no longer needs to be freed.
		/// </remarks>
		public async Task<RenameExecutionResult> ExecuteAsync(IReadOnlyList<RenameExecutionItem> plan, IFilesystemHelpers fs, IProgress<string>? progress, CancellationToken ct)
		{
			ArgumentNullException.ThrowIfNull(plan);
			ArgumentNullException.ThrowIfNull(fs);

			List<(string Path, string Error)> failed = [];
			List<IStorageItemWithPath> sources = new(plan.Count);
			List<IStorageItemWithPath> destinations = new(plan.Count);

			try
			{
				for (int i = 0; i < plan.Count; i++)
				{
					ct.ThrowIfCancellationRequested();

					RenameExecutionItem item = plan[i];
					progress?.Report($"Renaming {i + 1}/{plan.Count}: {Path.GetFileName(item.SourcePath)}");

					try
					{
						string? overwriteError = await FreeDestinationAsync(item, fs);
						if (overwriteError is not null)
						{
							failed.Add((item.SourcePath, overwriteError));
							continue;
						}

						FilesystemItemType itemType = GetItemType(item.SourcePath);
						IStorageItemWithPath source = StorageHelpers.FromPathAndType(item.SourcePath, itemType);
						ReturnResult result = await fs.RenameAsync(
							source,
							Path.GetFileName(item.DestinationPath),
							NameCollisionOption.FailIfExists,
							registerHistory: false,
							showExtensionDialog: false);

						if (result == ReturnResult.Success)
						{
							sources.Add(source);
							destinations.Add(StorageHelpers.FromPathAndType(item.DestinationPath, itemType));
						}
						else
						{
							failed.Add((item.SourcePath, $"Rename failed ({result})."));
						}
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						failed.Add((item.SourcePath, ex.Message));
					}
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				// Renames that already ran stay undoable even though the batch was abandoned.
				RegisterHistory(sources, destinations);
				throw;
			}

			RegisterHistory(sources, destinations);

			return new RenameExecutionResult(failed);
		}

		// Sends the destination of an overwrite item to the recycle bin and returns null, or returns
		// the reason the destination could not be freed.
		private static async Task<string?> FreeDestinationAsync(RenameExecutionItem item, IFilesystemHelpers fs)
		{
			if (item.Status != ConflictStatus.WillOverwrite || !StorageHelpers.Exists(item.DestinationPath))
				return null;

			ReturnResult deleted = await fs.DeleteItemAsync(
				StorageHelpers.FromPathAndType(item.DestinationPath, GetItemType(item.DestinationPath)),
				DeleteConfirmationPolicies.Never,
				permanently: false,
				registerHistory: false);

			return deleted == ReturnResult.Success ? null : $"The destination could not be freed ({deleted}).";
		}

		// One merged rename history keeps the whole batch a single undo entry; it is registered as
		// soon as at least one rename succeeded.
		private static void RegisterHistory(List<IStorageItemWithPath> sources, List<IStorageItemWithPath> destinations)
		{
			if (sources.Count > 0)
				App.HistoryWrapper.AddHistory(new StorageHistory(FileOperationType.Rename, sources, destinations));
		}

		private static FilesystemItemType GetItemType(string path)
		{
			return Win32Helper.HasFileAttribute(path, SystemIO.FileAttributes.Directory)
				? FilesystemItemType.Directory
				: FilesystemItemType.File;
		}

		// A row runs when it is checked in and its rename actually changes the name; a case-only
		// change differs ordinally from the original and still runs.
		private static bool IsExecutable(RenamePreviewRow row)
		{
			if (!row.IsIncluded)
				return false;

			return row.Status switch
			{
				RenameRowStatus.Changed or RenameRowStatus.Manual or RenameRowStatus.Sequenced or RenameRowStatus.Overwrite
					=> !string.Equals(row.FinalName, row.Target.Name, StringComparison.Ordinal),
				_ => false,
			};
		}

		// True when the row's destination is the source of another pending row: renaming this row
		// first would collide with the name the other row still holds, so the other row goes first.
		private static bool VacatesAnotherRow(List<RenamePreviewRow> pending, int index)
		{
			string destination = DestinationPath(pending[index]);
			for (int i = 0; i < pending.Count; i++)
			{
				if (i != index && string.Equals(destination, pending[i].Target.FullPath, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		private static RenameExecutionItem ToItem(RenamePreviewRow row)
		{
			return new RenameExecutionItem(row.Target.FullPath, DestinationPath(row), row.Conflict.Status);
		}

		private static string DestinationPath(RenamePreviewRow row)
		{
			string? directory = Path.GetDirectoryName(row.Target.FullPath);
			return string.IsNullOrEmpty(directory) ? row.FinalName : Path.Combine(directory, row.FinalName);
		}
	}
}
