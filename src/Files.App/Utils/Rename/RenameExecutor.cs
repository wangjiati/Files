// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Builds the executable rename plan from preview rows.
	/// </summary>
	/// <remarks>
	/// Plan construction is a pure function of the rows. Rows that will not be renamed are
	/// dropped: unchanged rows, unchecked rows, skipped rows and error rows, plus rows whose
	/// final name equals their current name. Chained renames — where one row's destination is
	/// another row's source — are ordered so the destination is vacated first, which also makes
	/// the reverse of the plan a safe undo replay order. A swap cycle (a→b, b→a) has no such
	/// order; it is emitted in input order and execution reports the failure.
	/// </remarks>
	public static class RenameExecutor
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
