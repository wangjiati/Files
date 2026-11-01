// Copyright (c) Files Community
// Licensed under the MIT License.

// Alias: the row's UnchangedReason property shadows the enum name inside the class body.
using RowUnchangedReason = Files.App.Utils.Rename.UnchangedReason;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// The state a preview row is shown with.
	/// </summary>
	public enum RenameRowStatus
	{
		Unchanged,
		Changed,
		Manual,
		Sequenced,
		Skipped,
		Overwrite,
		Error,
	}

	/// <summary>
	/// Why a preview row stays unchanged.
	/// </summary>
	public enum UnchangedReason
	{
		NotMatched,
		ExcludedFolderType,
	}

	/// <summary>
	/// One row of the batch rename preview: the target, the rule outcome and the conflict decision
	/// it was built from, plus the manual override the user may have applied to it.
	/// </summary>
	/// <remarks>
	/// Manual names survive every preview rebuild; they win over both the rule and the conflict
	/// decision until <see cref="ResetManual"/> is called. A manual name that fails
	/// <see cref="RenameValidator.Validate"/> marks the row <see cref="RenameRowStatus.Error"/>
	/// and keeps it out of the execution plan, without blocking the rest of the batch. A folder
	/// row built while folders are excluded from the batch is always
	/// <see cref="RenameRowStatus.Unchanged"/> with <see cref="UnchangedReason.ExcludedFolderType"/>,
	/// regardless of what the rule would have done.
	/// </remarks>
	public sealed class RenamePreviewRow(
		RenameTarget Target,
		RenameOutcome Outcome,
		ConflictOutcome Conflict,
		bool IncludeFolders = true)
	{
		public RenameTarget Target { get; } = Target;

		public RenameOutcome Outcome { get; } = Outcome;

		public ConflictOutcome Conflict { get; } = Conflict;

		/// <summary>
		/// The name the user typed for this row, if any; it overrides every computed name.
		/// </summary>
		public string? ManualName { get; private set; }

		/// <summary>
		/// Whether this row participates in the batch when it is applied.
		/// </summary>
		public bool IsIncluded { get; set; } = true;

		/// <summary>
		/// Whether the row's final name duplicates another row's final name in the same folder; the
		/// preview pass over the rows sets it, because a manual override bypasses conflict resolution.
		/// </summary>
		public bool HasDuplicateFinalName { get; internal set; }

		/// <summary>
		/// The name the item ends up with: the manual name, else the conflict-resolved name.
		/// </summary>
		public string FinalName => ManualName ?? Conflict.FinalName ?? Outcome.OriginalName;

		/// <summary>
		/// Why the row is in error; <see cref="RenameError.None"/> when it is not.
		/// </summary>
		public RenameError RowError
		{
			get
			{
				if (ManualName is not null)
				{
					if (RenameValidator.Validate(ManualName) is not null)
						return RenameError.InvalidName;
					if (HasDuplicateFinalName)
						return RenameError.DuplicateName;

					return RenameError.None;
				}

				return Outcome.Kind == RenameOutcomeKind.RuleError ? Outcome.Error : RenameError.None;
			}
		}

		public RenameRowStatus Status
		{
			get
			{
				// A manual name that fails name validation flags the row as an error and keeps it
				// out of the plan, but the typed value stays until the user resets the row.
				if (RowError != RenameError.None)
					return RenameRowStatus.Error;
				if (ManualName is not null)
					return RenameRowStatus.Manual;
				if (Target.IsFolder && !IncludeFolders)
					return RenameRowStatus.Unchanged;
				if (Outcome.Kind == RenameOutcomeKind.RuleError)
					return RenameRowStatus.Error;
				if (Outcome.Kind == RenameOutcomeKind.Unchanged)
					return RenameRowStatus.Unchanged;

				return Conflict.Status switch
				{
					ConflictStatus.WillSkip => RenameRowStatus.Skipped,
					ConflictStatus.WillOverwrite => RenameRowStatus.Overwrite,
					ConflictStatus.WillAppendNumber => RenameRowStatus.Sequenced,
					_ => RenameRowStatus.Changed,
				};
			}
		}

		/// <summary>
		/// Why an unchanged row stays unchanged; <c>null</c> for every other status.
		/// </summary>
		public UnchangedReason? UnchangedReason
		{
			get
			{
				if (Status != RenameRowStatus.Unchanged)
					return null;

				return Target.IsFolder && !IncludeFolders
					? RowUnchangedReason.ExcludedFolderType
					: RowUnchangedReason.NotMatched;
			}
		}

		/// <summary>
		/// Sets the manual override name for this row.
		/// </summary>
		public void SetManual(string name)
		{
			ManualName = name;
		}

		/// <summary>
		/// Clears the manual override; the row falls back to the computed rule result.
		/// </summary>
		public void ResetManual()
		{
			ManualName = null;
		}
	}
}
