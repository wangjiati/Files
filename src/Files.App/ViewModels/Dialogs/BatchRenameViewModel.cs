// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Files.App.Utils.Rename;

namespace Files.App.ViewModels.Dialogs
{
	/// <summary>
	/// The filter segments of the batch rename preview.
	/// </summary>
	public enum PreviewFilter
	{
		All,
		Changed,
		Sequenced,
	}

	/// <summary>
	/// Per-status row totals of the current preview.
	/// </summary>
	public sealed record RenamePreviewCounts(
		int Total,
		int Unchanged,
		int Changed,
		int Manual,
		int Sequenced,
		int Skipped,
		int Overwrite,
		int Error);

	/// <summary>
	/// Drives the batch rename dialog: scope resolution, the rule, the preview rows and the apply flow.
	/// </summary>
	/// <remarks>
	/// The view model never touches the file system itself: scope resolution goes through the
	/// injected <see cref="IRenameScopeResolver"/> and execution through the injected
	/// <see cref="IRenameExecutor"/>. Call <see cref="SetSelection"/> and then
	/// <see cref="ResolveScopeAsync"/> before the preview can show anything. Settings such as the
	/// docking side and the last preset name belong to the overlay, not to this class.
	/// </remarks>
	public sealed partial class BatchRenameViewModel : ObservableObject
	{
		private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(200);

		private readonly IRenameScopeResolver scopeResolver;
		private readonly IRenameExecutor executor;
		private readonly RenameEngine engine = new();

		private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoFolderNames = new Dictionary<string, IReadOnlyList<string>>();

		private IReadOnlyList<ListedItem> selectedItems = [];
		private ListedItem? currentFolderItem;
		private IReadOnlyList<RenameTarget> targets = [];
		private IReadOnlyDictionary<string, IReadOnlyList<string>> existingFolderNames = NoFolderNames;
		private CancellationTokenSource? updateCts;

		public BatchRenameViewModel(IRenameScopeResolver scopeResolver, IRenameExecutor executor)
		{
			this.scopeResolver = scopeResolver;
			this.executor = executor;
			ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
			CancelResolveCommand = new RelayCommand(CancelPendingUpdate);
		}

		// Inputs

		/// <summary>
		/// The file system the executor runs against; the overlay sets it before applying.
		/// </summary>
		public IFilesystemHelpers? Filesystem { get; set; }

		[ObservableProperty]
		public partial RenameRule Rule { get; set; } = new();

		[ObservableProperty]
		public partial RenameScopeOption Scope { get; set; } = RenameScopeOption.SelectedItems;

		[ObservableProperty]
		public partial int Depth { get; set; } = 2;

		[ObservableProperty]
		public partial bool IncludeFiles { get; set; } = true;

		[ObservableProperty]
		public partial bool IncludeFolders { get; set; }

		[ObservableProperty]
		public partial ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.AppendNumber;

		[ObservableProperty]
		public partial PreviewFilter PreviewFilter { get; set; } = PreviewFilter.Changed;

		[ObservableProperty]
		public partial string SearchText { get; set; } = string.Empty;

		[ObservableProperty]
		public partial bool IsResolving { get; private set; }

		[ObservableProperty]
		public partial string? ResolveError { get; private set; }

		[ObservableProperty]
		public partial bool PasteTruncated { get; private set; }

		// Outputs

		/// <summary>
		/// All preview rows, in target order.
		/// </summary>
		public IReadOnlyList<RenamePreviewRow> Rows { get; private set; } = [];

		/// <summary>
		/// The rows left after the filter segment and the search text are applied.
		/// </summary>
		public IReadOnlyList<RenamePreviewRow> VisibleRows
		{
			get
			{
				List<RenamePreviewRow> visible = new(Rows.Count);
				for (int i = 0; i < Rows.Count; i++)
				{
					if (MatchesFilter(Rows[i]) && MatchesSearch(Rows[i], i + 1))
						visible.Add(Rows[i]);
				}

				return visible;
			}
		}

		/// <summary>
		/// Whether the rule itself is broken and no apply is possible at all.
		/// </summary>
		public bool IsRuleBroken => Rows.Any(row => row.Outcome.Error is RenameError.BadPattern or RenameError.RegexTimeout);

		/// <summary>
		/// Whether an apply has anything to execute: the rule is intact and at least one row enters the plan.
		/// </summary>
		/// <remarks>
		/// This is the existence form of <see cref="RenameExecutor.BuildExecutionPlan"/>, not a plan
		/// build: it runs on every counts refresh, so it stays linear in the row count.
		/// </remarks>
		public bool CanApply
		{
			get
			{
				if (IsRuleBroken)
					return false;

				foreach (RenamePreviewRow row in Rows)
				{
					if (RenameExecutor.IsExecutable(row))
						return true;
				}

				return false;
			}
		}

		public RenamePreviewCounts Counts { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0);

		// Commands

		public IAsyncRelayCommand ApplyCommand { get; }

		/// <summary>
		/// Cancels the pending scope resolution or rebuild, and the enumeration it drives.
		/// </summary>
		public IRelayCommand CancelResolveCommand { get; }

		// Scope

		/// <summary>
		/// Stores the selection inputs the scope is resolved from. Does not resolve; the caller
		/// decides when to call <see cref="ResolveScopeAsync"/>.
		/// </summary>
		public void SetSelection(IReadOnlyList<ListedItem> selectedItems, ListedItem? currentFolderItem)
		{
			this.selectedItems = selectedItems;
			this.currentFolderItem = currentFolderItem;
		}

		/// <summary>
		/// Resolves the current scope into targets and rebuilds the preview from them.
		/// </summary>
		/// <remarks>
		/// A failure is surfaced through <see cref="ResolveError"/> instead of thrown; a
		/// cancellation is dropped silently because it means a newer update superseded this one.
		/// </remarks>
		public async Task ResolveScopeAsync(CancellationToken ct = default)
		{
			try
			{
				IsResolving = true;
				ResolveError = null;
				RenameScopeResult scope = await scopeResolver.ResolveAsync(Scope, Depth, IncludeFiles, IncludeFolders, selectedItems, currentFolderItem, ct);
				targets = scope.Targets;
				existingFolderNames = scope.ExistingNamesInFolders;
				RebuildPreview();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				ResolveError = ex.Message;
			}
			finally
			{
				IsResolving = false;
			}
		}

		// Preview

		/// <summary>
		/// Rebuilds every row from the resolved targets under the current rule and conflict strategy.
		/// Manual names and per-row inclusion flags are carried over by target path.
		/// </summary>
		public void RebuildPreview()
		{
			Dictionary<string, (string? ManualName, bool IsIncluded)> carried = new(StringComparer.OrdinalIgnoreCase);
			foreach (RenamePreviewRow row in Rows)
				carried[row.Target.FullPath] = (row.ManualName, row.IsIncluded);

			List<RenamePreviewRow> built = new(targets.Count);
			if (targets.Count > 0)
				built.AddRange(ComputeRows(carried));

			FlagDuplicateFinalNames(built);
			Rows = built;
			RefreshCounts();
			OnPropertyChanged(nameof(Rows));
			OnPropertyChanged(nameof(VisibleRows));
			OnPropertyChanged(nameof(IsRuleBroken));
			ApplyCommand.NotifyCanExecuteChanged();
		}

		private IEnumerable<RenamePreviewRow> ComputeRows(Dictionary<string, (string? ManualName, bool IsIncluded)> carried)
		{
			IReadOnlyList<RenameOutcome> outcomes = engine.Run(targets, Rule);

			// Seed every folder's occupied-name set with all original names in it and the sibling
			// names the resolver reported, so a destination that already exists conflicts too; the
			// resolver's two passes account for same-batch collisions on top of that.
			Dictionary<string, HashSet<string>> folderNames = new(StringComparer.OrdinalIgnoreCase);
			foreach (RenameTarget target in targets)
			{
				string folder = GetFolderPath(target);
				if (!folderNames.TryGetValue(folder, out HashSet<string>? names))
					folderNames[folder] = names = new(StringComparer.OrdinalIgnoreCase);
				names.Add(target.Name);
			}

			foreach ((string folder, IReadOnlyList<string> names) in existingFolderNames)
			{
				if (!folderNames.TryGetValue(folder, out HashSet<string>? set))
					folderNames[folder] = set = new(StringComparer.OrdinalIgnoreCase);
				foreach (string name in names)
					set.Add(name);
			}

			List<ConflictInput> inputs = new(targets.Count);
			foreach (RenameTarget target in targets)
			{
				string folder = GetFolderPath(target);
				inputs.Add(new ConflictInput(target.FullPath, folder, target.Name, outcomes[inputs.Count].NewName, folderNames[folder]));
			}

			IReadOnlyList<ConflictOutcome> conflicts = RenameConflictResolver.Resolve(inputs, ConflictStrategy);

			for (int i = 0; i < targets.Count; i++)
			{
				var row = new RenamePreviewRow(targets[i], outcomes[i], conflicts[i], IncludeFolders);
				if (carried.TryGetValue(targets[i].FullPath, out (string? ManualName, bool IsIncluded) saved))
				{
					if (saved.ManualName is not null)
						row.SetManual(saved.ManualName);
					row.IsIncluded = saved.IsIncluded;
				}

				yield return row;
			}
		}

		/// <summary>
		/// Flags manual names whose final name another row also ends up with in the same folder: they
		/// bypass conflict resolution, so the duplicate is caught here instead. Only manual rows are
		/// flagged; computed names are already unique per folder.
		/// </summary>
		private static void FlagDuplicateFinalNames(IReadOnlyList<RenamePreviewRow> rows)
		{
			Dictionary<string, Dictionary<string, int>> finalNameCounts = new(StringComparer.OrdinalIgnoreCase);
			foreach (RenamePreviewRow row in rows)
			{
				string folder = GetFolderPath(row.Target);
				if (!finalNameCounts.TryGetValue(folder, out Dictionary<string, int>? names))
					finalNameCounts[folder] = names = new(StringComparer.OrdinalIgnoreCase);
				names.TryGetValue(row.FinalName, out int count);
				names[row.FinalName] = count + 1;
			}

			foreach (RenamePreviewRow row in rows)
				row.HasDuplicateFinalName = row.ManualName is not null &&
					finalNameCounts[GetFolderPath(row.Target)][row.FinalName] > 1;
		}

		// Rule shortcuts

		/// <summary>
		/// Switches the rule's template, keeping the scope, the case conversion and every manual edit.
		/// </summary>
		public void ApplyTemplate(RenameTemplate template)
		{
			Rule = Rule with { Template = template };
		}

		/// <summary>
		/// Fills a numbering/date pattern string into the replace-with field, touching nothing else.
		/// </summary>
		public void ApplyPatternPreset(string replaceText)
		{
			Rule = Rule with { ReplaceWith = replaceText };
		}

		// Clipboard

		/// <summary>
		/// Returns every row's original name, one per line, in row order.
		/// </summary>
		public string CopyOriginalNames()
		{
			return string.Join("\r\n", Rows.Select(row => row.Target.Name));
		}

		/// <summary>
		/// Applies pasted names to the rows in order: an empty line leaves its row untouched, extra
		/// lines are dropped and flagged through <see cref="PasteTruncated"/>.
		/// </summary>
		public void PasteNewNames(string text)
		{
			PasteTruncated = false;
			if (Rows.Count == 0 || string.IsNullOrEmpty(text))
				return;

			string[] lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
			for (int i = 0; i < Rows.Count && i < lines.Length; i++)
			{
				if (lines[i].Length > 0)
					Rows[i].SetManual(lines[i]);
			}

			if (lines.Length > Rows.Count)
				PasteTruncated = true;

			FlagDuplicateFinalNames(Rows);
			RefreshCounts();
			OnPropertyChanged(nameof(VisibleRows));
			ApplyCommand.NotifyCanExecuteChanged();
		}

		/// <summary>
		/// Sets a row's manual name and refreshes the derived state.
		/// </summary>
		public void SetManualName(RenamePreviewRow row, string name)
		{
			row.SetManual(name);
			FlagDuplicateFinalNames(Rows);
			RefreshCounts();
			OnPropertyChanged(nameof(VisibleRows));
			ApplyCommand.NotifyCanExecuteChanged();
		}

		/// <summary>
		/// Clears a row's manual name and refreshes the derived state.
		/// </summary>
		public void ResetManualName(RenamePreviewRow row)
		{
			row.ResetManual();
			FlagDuplicateFinalNames(Rows);
			RefreshCounts();
			OnPropertyChanged(nameof(VisibleRows));
			ApplyCommand.NotifyCanExecuteChanged();
		}

		// Apply

		private async Task ApplyAsync()
		{
			IReadOnlyList<RenameExecutionItem> plan = RenameExecutor.BuildExecutionPlan(Rows, ConflictStrategy);
			if (plan.Count == 0 || Filesystem is null)
				return;

			await executor.ExecuteAsync(plan, Filesystem, null, CancellationToken.None);
		}

		// Debounced updates

		partial void OnRuleChanged(RenameRule value) => SchedulePreviewUpdate();

		partial void OnScopeChanged(RenameScopeOption value) => SchedulePreviewUpdate(resolveScopeFirst: true);

		partial void OnDepthChanged(int value)
		{
			if (value is < 1 or > 9)
			{
				Depth = Math.Clamp(value, 1, 9);
				return;
			}

			SchedulePreviewUpdate(resolveScopeFirst: true);
		}

		partial void OnIncludeFilesChanged(bool value) => SchedulePreviewUpdate(resolveScopeFirst: true);

		partial void OnIncludeFoldersChanged(bool value) => SchedulePreviewUpdate(resolveScopeFirst: true);

		partial void OnConflictStrategyChanged(ConflictStrategy value) => SchedulePreviewUpdate();

		partial void OnPreviewFilterChanged(PreviewFilter value) => OnPropertyChanged(nameof(VisibleRows));

		partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleRows));

		private void SchedulePreviewUpdate(bool resolveScopeFirst = false)
		{
			updateCts?.Cancel();
			updateCts = new CancellationTokenSource();
			_ = DebouncedUpdateAsync(resolveScopeFirst, updateCts.Token);
		}

		private async Task DebouncedUpdateAsync(bool resolveScopeFirst, CancellationToken ct)
		{
			try
			{
				await Task.Delay(RebuildDelay, ct);
				if (resolveScopeFirst)
					await ResolveScopeAsync(ct);
				else
					RebuildPreview();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void CancelPendingUpdate()
		{
			updateCts?.Cancel();
		}

		// Helpers

		private bool MatchesFilter(RenamePreviewRow row)
		{
			return PreviewFilter switch
			{
				PreviewFilter.Changed => row.Status
					is RenameRowStatus.Changed or RenameRowStatus.Manual or RenameRowStatus.Sequenced or RenameRowStatus.Overwrite,
				PreviewFilter.Sequenced => row.Status is RenameRowStatus.Sequenced or RenameRowStatus.Skipped,
				_ => true,
			};
		}

		private bool MatchesSearch(RenamePreviewRow row, int position)
		{
			return SearchText.Length == 0
				|| row.Target.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
				|| row.FinalName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
				|| position.ToString().Contains(SearchText, StringComparison.Ordinal);
		}

		private void RefreshCounts()
		{
			int unchanged = 0, changed = 0, manual = 0, sequenced = 0, skipped = 0, overwrite = 0, error = 0;
			foreach (RenamePreviewRow row in Rows)
			{
				switch (row.Status)
				{
					case RenameRowStatus.Changed: changed++; break;
					case RenameRowStatus.Manual: manual++; break;
					case RenameRowStatus.Sequenced: sequenced++; break;
					case RenameRowStatus.Skipped: skipped++; break;
					case RenameRowStatus.Overwrite: overwrite++; break;
					case RenameRowStatus.Error: error++; break;
					default: unchanged++; break;
				}
			}

			Counts = new RenamePreviewCounts(Rows.Count, unchanged, changed, manual, sequenced, skipped, overwrite, error);
			OnPropertyChanged(nameof(Counts));
		}

		private static string GetFolderPath(RenameTarget target)
		{
			return Path.GetDirectoryName(target.FullPath) ?? string.Empty;
		}
	}
}
