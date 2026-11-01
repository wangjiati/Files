// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI;
using Files.App.Utils.Rename;
using Files.App.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Files.App.UserControls
{
	/// <summary>
	/// Hosts the batch rename experience as an overlay on top of the main page: the rule panel, the
	/// grouped preview list with inline editing, presets and the apply flow.
	/// </summary>
	/// <remarks>
	/// The overlay owns none of the rename logic; it drives <see cref="BatchRenameViewModel"/> and
	/// forwards every row edit back through it so counts and the apply gate stay consistent. While
	/// <see cref="IsOpen"/> is true, MainPage suppresses its global hotkey dispatch (including the
	/// Ctrl+H "show hidden items" accelerator), and this control handles Ctrl+H, Esc, F2 and Enter itself.
	/// </remarks>
	public sealed partial class BatchRenameOverlayPresenter : UserControl, INotifyPropertyChanged
	{
		/// <summary>Pattern filled in by the numbering preset button.</summary>
		private const string NumberingPattern = "Photos_${padding=3;start=1}";

		/// <summary>Pattern filled in by the date preset button.</summary>
		private const string DatePattern = "$YYYY-$MM-$DD_${padding=3}";

		private const string CounterVariable = "${padding=3;start=1}";
		private const string DateVariable = "$YYYY-$MM-$DD";
		private const string FolderVariable = "${folder}";

		public static BatchRenameOverlayPresenter? Instance { get; private set; }

		public bool IsOpen { get; private set; }

		public event PropertyChangedEventHandler? PropertyChanged;

		private BatchRenameViewModel? vm;
		private IShellPage? shellPage;
		private string rootPath = string.Empty;
		private RenamePresetStore presetStore = null!;
		private List<RenamePreset> presets = [];
		private List<BatchRenameRowViewModel> wrappers = [];
		private Dictionary<RenamePreviewRow, BatchRenameRowViewModel> wrapperByRow = new();
		private IReadOnlyList<RenamePreviewRow> lastRebuiltRows = [];
		private BatchRenameRowViewModel? editingRow;
		private Progress<string>? applyProgress;
		private UiRenameExecutor? executor;
		private bool isApplying;
		private bool isSyncingControls;

		/// <summary>Exposed for x:Bind paths rooted at the active view model.</summary>
		public BatchRenameViewModel? Vm => vm;

		public BatchRenameOverlayPresenter()
		{
			InitializeComponent();

			Loaded += BatchRenameOverlayPresenter_Loaded;
			Unloaded += BatchRenameOverlayPresenter_Unloaded;
			ActualThemeChanged += BatchRenameOverlayPresenter_ActualThemeChanged;
		}

		private void BatchRenameOverlayPresenter_Loaded(object sender, RoutedEventArgs e)
		{
			Instance = this;
			RefreshStyleCache();
		}

		private void BatchRenameOverlayPresenter_Unloaded(object sender, RoutedEventArgs e)
		{
			if (Instance == this)
				Instance = null;
		}

		private void BatchRenameOverlayPresenter_ActualThemeChanged(FrameworkElement sender, object args)
		{
			RefreshStyleCache();
			if (vm is not null)
				RebuildRows();
		}

		// Open / close

		public void Open(IShellPage shellPage, IReadOnlyList<ListedItem> selectedItems)
		{
			ArgumentNullException.ThrowIfNull(shellPage);
			ArgumentNullException.ThrowIfNull(selectedItems);

			ResetSession();

			this.shellPage = shellPage;
			rootPath = shellPage.ShellViewModel?.CurrentFolder?.ItemPath ?? string.Empty;
			presetStore = new RenamePresetStore(Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.RenamePresetsFileName));

			var renameExecutor = new UiRenameExecutor(new RenameExecutor(), () => applyProgress);
			executor = renameExecutor;
			vm = new BatchRenameViewModel(new RenameScopeResolver(), renameExecutor)
			{
				Filesystem = shellPage.FilesystemHelpers,
			};
			vm.SetSelection(selectedItems, shellPage.ShellViewModel?.CurrentFolder);
			vm.PropertyChanged += Vm_PropertyChanged;
			vm.ApplyCommand.CanExecuteChanged += ApplyCommand_CanExecuteChanged;
			applyProgress = new Progress<string>(ReportApplyProgress);

			// The ruling pins the default scope: the selection when there is one, otherwise the current folder.
			var initialScope = selectedItems.Count > 0 ? RenameScopeOption.SelectedItems : RenameScopeOption.CurrentFolder;
			if (vm.Scope == initialScope)
			{
				// No scope change event will fire, so resolve explicitly.
				_ = vm.ResolveScopeAsync();
			}
			else
			{
				vm.Scope = initialScope;
			}

			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vm)));
			SyncControlsFromVm();
			ResetSessionUi();
			RebuildRows();
			ApplyDockState(Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService.RulePanelDockedLeft);
			VisualStateManager.GoToState(this, "NormalSizeState", true);
			_ = LoadPresetsAsync();

			IsOpen = true;
			Visibility = Visibility.Visible;

			DispatcherQueue.TryEnqueue(() =>
			{
				if (IsOpen)
					SearchBox.Focus(FocusState.Programmatic);
			});
		}

		public void Close()
		{
			// Same semantics as the disabled Cancel button: an apply in flight cannot be dismissed.
			if (isApplying)
				return;

			if (!IsOpen && vm is null)
				return;

			IsOpen = false;
			Visibility = Visibility.Collapsed;
			ResetSession();
		}

		private void ResetSession()
		{
			if (vm is not null)
			{
				vm.CancelResolveCommand.Execute(null);
				vm.PropertyChanged -= Vm_PropertyChanged;
				vm.ApplyCommand.CanExecuteChanged -= ApplyCommand_CanExecuteChanged;
			}

			vm = null;
			shellPage = null;
			editingRow = null;
			wrappers = [];
			wrapperByRow = new();
			lastRebuiltRows = [];
			presets = [];
			RowsView.Source = null;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vm)));
		}

		private void ResetSessionUi()
		{
			NoticeText.Text = string.Empty;
			ProgressText.Text = string.Empty;
			ResolveErrorText.Text = string.Empty;
			ResolveErrorText.Visibility = Visibility.Collapsed;
			StatusPanel.Visibility = Visibility.Collapsed;
			ApplyRing.IsActive = false;
			isApplying = false;
		}

		// View model change handling

		private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (vm is null)
				return;

			switch (e.PropertyName)
			{
				case nameof(BatchRenameViewModel.Rows):
					RebuildRows();
					break;
				case nameof(BatchRenameViewModel.VisibleRows):
					if (!ReferenceEquals(lastRebuiltRows, vm.Rows))
						RebuildRows();
					else
						UpdateList();
					break;
				case nameof(BatchRenameViewModel.Counts):
					UpdateStatusUi();
					break;
				case nameof(BatchRenameViewModel.IsResolving):
					UpdateStatusUi();
					break;
				case nameof(BatchRenameViewModel.ResolveError):
					ResolveErrorText.Text = vm.ResolveError is null
						? string.Empty
						: string.Format(Strings.BatchRenameResolveFailed.GetLocalizedResource(), vm.ResolveError);
					ResolveErrorText.Visibility = vm.ResolveError is null ? Visibility.Collapsed : Visibility.Visible;
					break;
				case nameof(BatchRenameViewModel.PasteTruncated):
					if (vm.PasteTruncated)
						SetNotice(Strings.BatchRenamePasteTruncated.GetLocalizedResource());
					break;
				case nameof(BatchRenameViewModel.Rule):
				case nameof(BatchRenameViewModel.Scope):
				case nameof(BatchRenameViewModel.Depth):
				case nameof(BatchRenameViewModel.IncludeFiles):
				case nameof(BatchRenameViewModel.IncludeFolders):
				case nameof(BatchRenameViewModel.ConflictStrategy):
				case nameof(BatchRenameViewModel.PreviewFilter):
					SyncControlsFromVm();
					UpdateStatusUi();
					break;
			}
		}

		private void ApplyCommand_CanExecuteChanged(object? sender, EventArgs e)
		{
			UpdateApplyUi();
		}

		/// <summary>Recreates every row wrapper from the view model's current rows, then regroups the list.</summary>
		private void RebuildRows()
		{
			if (vm is null)
				return;

			RefreshStyleCache();
			bool withSubfolders = vm.Scope == RenameScopeOption.CurrentFolderWithSubfolders;

			wrappers = new List<BatchRenameRowViewModel>(vm.Rows.Count);
			wrapperByRow = new Dictionary<RenamePreviewRow, BatchRenameRowViewModel>(vm.Rows.Count);
			foreach (RenamePreviewRow row in vm.Rows)
			{
				(string group, string position) = ComputeGroupAndPosition(row, rootPath, withSubfolders);
				BatchRenameRowViewModel wrapper = new(row, group, position, withSubfolders, OnRowStateChanged);
				wrappers.Add(wrapper);
				wrapperByRow[row] = wrapper;
			}

			lastRebuiltRows = vm.Rows;
			UpdateList();
		}

		/// <summary>Regroups the visible rows into the list and refreshes every derived status area.</summary>
		private void UpdateList()
		{
			if (vm is null)
				return;

			List<BatchRenameRowGroup> groups = [];
			BatchRenameRowGroup? current = null;
			foreach (RenamePreviewRow row in vm.VisibleRows)
			{
				if (!wrapperByRow.TryGetValue(row, out BatchRenameRowViewModel? wrapper))
					continue;

				if (current is null || current.Key != wrapper.GroupHeader)
				{
					current = new([wrapper]) { Key = wrapper.GroupHeader };
					groups.Add(current);
				}
				else
				{
					current.Add(wrapper);
				}
			}

			RowsView.Source = groups;
			UpdateStatusUi();
		}

		private static (string Group, string Position) ComputeGroupAndPosition(RenamePreviewRow row, string rootPath, bool withSubfolders)
		{
			string dir = Path.GetDirectoryName(row.Target.FullPath) ?? string.Empty;
			if (!withSubfolders)
				return (Strings.BatchRenameGroupCurrentFolder.GetLocalizedResource(), string.Empty);

			// Paths are relative to the current folder itself: a "D:\photos\京都" under the root
			// "D:\photos" shows group "京都 · Level 1" and position "京都\", matching the depth
			// stepper's "levels 1–N". A drive root keeps its label as the group.
			string root = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (root.Length == 0)
				return (dir, dir);

			if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase) || string.Equals(dir, rootPath, StringComparison.OrdinalIgnoreCase))
				return (Strings.BatchRenameGroupCurrentFolder.GetLocalizedResource(), ".");

			if (
				dir.Length > root.Length + 1 &&
				(dir[root.Length] == Path.DirectorySeparatorChar || dir[root.Length] == Path.AltDirectorySeparatorChar) &&
				dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				string relative = dir[(root.Length + 1)..];
				int level = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
				return (
					string.Format(Strings.BatchRenameGroupLevelFormat.GetLocalizedResource(), relative, level),
					relative + Path.DirectorySeparatorChar);
			}

			return (dir, dir);
		}

		private void UpdateStatusUi()
		{
			if (vm is null)
				return;

			RenamePreviewCounts counts = vm.Counts;
			int toChange = counts.Changed + counts.Manual + counts.Sequenced + counts.Overwrite;
			int conflicts = counts.Sequenced + counts.Skipped + counts.Overwrite;
			int excluded = wrappers.Count(wrapper => !wrapper.Source.IsIncluded);

			FilterAllItem.Content = $"{Strings.BatchRenameFilterAll.GetLocalizedResource()} ({counts.Total})";
			FilterChangedItem.Content = $"{Strings.BatchRenameFilterChanged.GetLocalizedResource()} ({toChange})";
			FilterSequencedItem.Content = $"{Strings.BatchRenameFilterSequenced.GetLocalizedResource()} ({counts.Sequenced + counts.Skipped})";

			StatsText.Text = string.Format(
				Strings.BatchRenameStatsFormat.GetLocalizedResource(),
				toChange,
				counts.Sequenced,
				counts.Unchanged,
				excluded);

			ConflictPanel.Visibility = conflicts > 0 || counts.Error > 0 ? Visibility.Visible : Visibility.Collapsed;
			ConflictText.Text = string.Format(
				Strings.BatchRenameConflictSummaryFormat.GetLocalizedResource(),
				conflicts,
				counts.Error);

			string scopeSummary = vm.Scope switch
			{
				RenameScopeOption.SelectedItems => Strings.BatchRenameScopeSelectedItems.GetLocalizedResource(),
				RenameScopeOption.CurrentFolder => Strings.BatchRenameScopeCurrentFolder.GetLocalizedResource(),
				_ => string.Format(Strings.BatchRenameScopeWithSubfoldersFormat.GetLocalizedResource(), vm.Depth),
			};
			SubtitleText.Text = string.Format(
				Strings.BatchRenameSubtitleFormat.GetLocalizedResource(),
				rootPath,
				scopeSummary,
				counts.Total);

			ScopeSelectedCountText.Text = vm.Scope == RenameScopeOption.SelectedItems ? counts.Total.ToString() : string.Empty;
			ScopeFolderCountText.Text = vm.Scope == RenameScopeOption.CurrentFolder ? counts.Total.ToString() : string.Empty;
			ScopeSubfoldersCountText.Text = vm.Scope == RenameScopeOption.CurrentFolderWithSubfolders ? counts.Total.ToString() : string.Empty;

			bool withSubfolders = vm.Scope == RenameScopeOption.CurrentFolderWithSubfolders;
			HeaderPositionCell.Visibility = withSubfolders ? Visibility.Visible : Visibility.Collapsed;
			DepthPanel.Visibility = withSubfolders ? Visibility.Visible : Visibility.Collapsed;

			ResolvingRing.IsActive = vm.IsResolving;
			EmptyStatePanel.Visibility = vm.IsResolving || vm.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			EmptyStateText.Text = string.Empty;

			UpdateApplyUi();
		}

		private void UpdateApplyUi()
		{
			if (vm is null)
				return;

			ApplyButton.IsEnabled = !isApplying && vm.ApplyCommand.CanExecute(null);
			CancelButton.IsEnabled = !isApplying;
			ApplyRing.IsActive = isApplying;
			StatusPanel.Visibility = isApplying || NoticeText.Text.Length > 0 || vm.IsResolving
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		private void OnRowStateChanged()
		{
			vm?.ApplyCommand.NotifyCanExecuteChanged();
			UpdateStatusUi();
		}

		private void SetNotice(string text)
		{
			NoticeText.Text = text;
			UpdateApplyUi();
		}

		private void RefreshStyleCache()
		{
			string key = ActualTheme switch
			{
				ElementTheme.Dark => "Dark",
				ElementTheme.Light => "Light",
				_ => "Default",
			};

			if (Resources.ThemeDictionaries.TryGetValue(key, out object? dictionary) || Resources.ThemeDictionaries.TryGetValue("Default", out dictionary))
				BatchRenameRowStyle.Refresh((ResourceDictionary)dictionary!);
		}

		private void SyncControlsFromVm()
		{
			if (vm is null)
				return;

			isSyncingControls = true;

			RenameRule rule = vm.Rule;
			FindBox.Text = rule.Find;
			ReplaceBox.Text = rule.ReplaceWith;
			PrefixBox.Text = rule.Prefix;
			SuffixBox.Text = rule.Suffix;
			MatchAllCheck.IsChecked = rule.MatchAllOccurrences;
			CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
			SkipPrefixCheck.IsChecked = rule.SkipItemsWithPrefix;
			InsertBeforeExtensionCheck.IsChecked = rule.InsertBeforeExtension;
			TemplateSegmented.SelectedIndex = (int)rule.Template;
			ModeSegmented.SelectedIndex = (int)rule.Mode;
			ApplyToSegmented.SelectedIndex = (int)rule.ApplyTo;
			CaseCombo.SelectedIndex = (int)rule.CaseConversion;
			FindReplacePanel.Visibility = rule.Template == RenameTemplate.FindReplace ? Visibility.Visible : Visibility.Collapsed;
			PrependPanel.Visibility = rule.Template == RenameTemplate.Prepend ? Visibility.Visible : Visibility.Collapsed;
			AppendPanel.Visibility = rule.Template == RenameTemplate.Append ? Visibility.Visible : Visibility.Collapsed;

			ScopeSelectedRadio.IsChecked = vm.Scope == RenameScopeOption.SelectedItems;
			ScopeFolderRadio.IsChecked = vm.Scope == RenameScopeOption.CurrentFolder;
			ScopeSubfoldersRadio.IsChecked = vm.Scope == RenameScopeOption.CurrentFolderWithSubfolders;
			DepthValueText.Text = vm.Depth.ToString();
			DepthRangeText.Text = string.Format(Strings.BatchRenameDepthRangeFormat.GetLocalizedResource(), vm.Depth);
			IncludeFilesCheck.IsChecked = vm.IncludeFiles;
			IncludeFoldersCheck.IsChecked = vm.IncludeFolders;
			StrategyCombo.SelectedIndex = (int)vm.ConflictStrategy;
			FilterSegmented.SelectedIndex = (int)vm.PreviewFilter;

			isSyncingControls = false;
		}

		// Keyboard

		private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			bool ctrl = HotKeyHelpers.GetCurrentKeyModifiers().HasFlag(KeyModifiers.Ctrl);

			if (ctrl && e.Key is VirtualKey.H)
			{
				// Ctrl+H focuses the search box while the overlay is open; the global accelerator is suppressed.
				e.Handled = true;
				SearchBox.Focus(FocusState.Programmatic);
				SearchBox.SelectAll();
			}
			else if (e.Key is VirtualKey.Escape)
			{
				e.Handled = true;
				if (editingRow is not null)
					CancelEdit(editingRow);
				else
					Close();
			}
			else if (e.Key is VirtualKey.F2)
			{
				e.Handled = true;
				MoveToNextEditableRow();
			}
			else if (e.Key is VirtualKey.Enter && !ctrl)
			{
				e.Handled = true;
				if (editingRow is not null)
					CommitEdit(editingRow);
				else
					_ = ApplyAsync();
			}
		}

		// Inline row editing

		private void NewNameCell_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is BatchRenameRowViewModel wrapper)
			{
				e.Handled = true;
				BeginEdit(wrapper);
			}
		}

		private void EditBox_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key is VirtualKey.Enter)
			{
				e.Handled = true;
				if (editingRow is not null)
					CommitEdit(editingRow);
			}
			else if (e.Key is VirtualKey.Escape)
			{
				e.Handled = true;
				if (editingRow is not null)
					CancelEdit(editingRow);
			}
		}

		private void CommitEditButton_Click(object sender, RoutedEventArgs e)
		{
			if (editingRow is not null)
				CommitEdit(editingRow);
		}

		private void CancelEditButton_Click(object sender, RoutedEventArgs e)
		{
			if (editingRow is not null)
				CancelEdit(editingRow);
		}

		private void ResetRowButton_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is not BatchRenameRowViewModel wrapper || vm is null)
				return;

			if (editingRow == wrapper)
			{
				wrapper.IsEditing = false;
				editingRow = null;
			}

			if (wrapperByRow.ContainsKey(wrapper.Source))
				vm.ResetManualName(wrapper.Source);
		}

		private void MoveToNextEditableRow()
		{
			if (wrappers.Count == 0)
				return;

			int start = editingRow is null ? wrappers.Count - 1 : wrappers.IndexOf(editingRow);
			if (editingRow is not null)
			{
				if (start >= 0)
					CommitEdit(editingRow);
				else
					CancelEdit(editingRow);
			}

			for (int offset = 1; offset <= wrappers.Count; offset++)
			{
				BatchRenameRowViewModel candidate = wrappers[(start + offset + wrappers.Count) % wrappers.Count];
				if (!candidate.Source.IsIncluded)
					continue;

				BeginEdit(candidate);
				return;
			}
		}

		private void BeginEdit(BatchRenameRowViewModel wrapper)
		{
			if (editingRow == wrapper)
				return;

			if (editingRow is not null)
			{
				if (wrappers.Contains(editingRow))
					CommitEdit(editingRow);
				else
					CancelEdit(editingRow);
			}

			editingRow = wrapper;
			wrapper.EditText = wrapper.Source.FinalName;
			wrapper.IsEditing = true;
			PreviewList.ScrollIntoView(wrapper);
			FocusEditBox(wrapper);
		}

		/// <summary>
		/// Focuses the row's edit box once its container is realized; scrolling to a virtualized row
		/// realizes it asynchronously, so the lookup is retried once on the next pass.
		/// </summary>
		private void FocusEditBox(BatchRenameRowViewModel wrapper)
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				if (editingRow != wrapper || PreviewList.ContainerFromItem(wrapper) is not ListViewItem container)
					return;

				if (container.FindDescendant<TextBox>() is { } box)
				{
					box.Focus(FocusState.Programmatic);
					box.SelectAll();
				}
			});
		}

		/// <summary>Commits the edit through the view model so counts and the apply gate refresh.</summary>
		private void CommitEdit(BatchRenameRowViewModel wrapper)
		{
			wrapper.IsEditing = false;
			if (editingRow == wrapper)
				editingRow = null;

			// A rebuild may have replaced the rows; only rows still present can be committed.
			if (vm is not null && wrapperByRow.ContainsKey(wrapper.Source))
				vm.SetManualName(wrapper.Source, wrapper.EditText);
		}

		private void CancelEdit(BatchRenameRowViewModel wrapper)
		{
			wrapper.IsEditing = false;
			if (editingRow == wrapper)
				editingRow = null;
		}

		// Rule inputs. RenameRule is an immutable record, so every control writes a new instance.

		private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { Find = FindBox.Text };
		}

		private void ReplaceBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { ReplaceWith = ReplaceBox.Text };
		}

		private void PrefixBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { Prefix = PrefixBox.Text };
		}

		private void SuffixBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { Suffix = SuffixBox.Text };
		}

		private void RuleFlag_CheckChanged(object sender, RoutedEventArgs e)
		{
			if (isSyncingControls || vm is null)
				return;

			vm.Rule = vm.Rule with
			{
				MatchAllOccurrences = MatchAllCheck.IsChecked == true,
				CaseSensitive = CaseSensitiveCheck.IsChecked == true,
				SkipItemsWithPrefix = SkipPrefixCheck.IsChecked == true,
				InsertBeforeExtension = InsertBeforeExtensionCheck.IsChecked == true,
			};
		}

		private void TemplateSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && TemplateSegmented.SelectedIndex >= 0)
				vm.ApplyTemplate((RenameTemplate)TemplateSegmented.SelectedIndex);
		}

		private void ModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && ModeSegmented.SelectedIndex >= 0)
				vm.Rule = vm.Rule with { Mode = (RenameMode)ModeSegmented.SelectedIndex };
		}

		private void ApplyToSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && ApplyToSegmented.SelectedIndex >= 0)
				vm.Rule = vm.Rule with { ApplyTo = (RenameApplyTo)ApplyToSegmented.SelectedIndex };
		}

		private void CaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && CaseCombo.SelectedIndex >= 0)
				vm.Rule = vm.Rule with { CaseConversion = (RenameCaseConversion)CaseCombo.SelectedIndex };
		}

		// Scope

		private void ScopeRadio_Checked(object sender, RoutedEventArgs e)
		{
			if (isSyncingControls || vm is null)
				return;

			vm.Scope = sender == ScopeSelectedRadio
				? RenameScopeOption.SelectedItems
				: sender == ScopeFolderRadio
					? RenameScopeOption.CurrentFolder
					: RenameScopeOption.CurrentFolderWithSubfolders;
		}

		private void DepthMinusButton_Click(object sender, RoutedEventArgs e)
		{
			if (vm is not null)
				vm.Depth--;
		}

		private void DepthPlusButton_Click(object sender, RoutedEventArgs e)
		{
			if (vm is not null)
				vm.Depth++;
		}

		private void IncludeFlag_CheckChanged(object sender, RoutedEventArgs e)
		{
			if (isSyncingControls || vm is null)
				return;

			vm.IncludeFiles = IncludeFilesCheck.IsChecked == true;
			vm.IncludeFolders = IncludeFoldersCheck.IsChecked == true;
		}

		// Variables

		private void AddCounterChip_Click(object sender, RoutedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { ReplaceWith = vm.Rule.ReplaceWith + CounterVariable };
		}

		private void AddDateChip_Click(object sender, RoutedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { ReplaceWith = vm.Rule.ReplaceWith + DateVariable };
		}

		private void AddFolderChip_Click(object sender, RoutedEventArgs e)
		{
			if (!isSyncingControls && vm is not null)
				vm.Rule = vm.Rule with { ReplaceWith = vm.Rule.ReplaceWith + FolderVariable };
		}

		private void NumberingPatternChip_Click(object sender, RoutedEventArgs e)
		{
			if (vm is null)
				return;

			vm.ApplyTemplate(RenameTemplate.FindReplace);
			vm.ApplyPatternPreset(NumberingPattern);
		}

		private void DatePatternChip_Click(object sender, RoutedEventArgs e)
		{
			if (vm is null)
				return;

			vm.ApplyTemplate(RenameTemplate.FindReplace);
			vm.ApplyPatternPreset(DatePattern);
		}

		// Clipboard

		private void CopyOriginalButton_Click(object sender, RoutedEventArgs e)
		{
			if (vm is null || vm.Rows.Count == 0)
				return;

			DataPackage package = new();
			package.SetText(vm.CopyOriginalNames());
			Clipboard.SetContent(package);
		}

		private async void PasteNewButton_Click(object sender, RoutedEventArgs e)
		{
			if (vm is null)
				return;

			DataPackageView content = Clipboard.GetContent();
			if (!content.Contains(StandardDataFormats.Text))
				return;

			string text = await content.GetTextAsync();
			vm.PasteNewNames(text);
		}

		// Presets

		private async Task LoadPresetsAsync()
		{
			try
			{
				presets = [.. await presetStore.LoadAsync()];
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "The rename presets could not be loaded.");
				presets = [];
			}

			isSyncingControls = true;
			PresetCombo.Items.Clear();
			foreach (RenamePreset preset in presets)
				PresetCombo.Items.Add(preset.Name);
			isSyncingControls = false;

			string last = Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService.LastPresetName;
			int index = presets.FindIndex(preset => string.Equals(preset.Name, last, StringComparison.OrdinalIgnoreCase));
			if (index >= 0)
			{
				isSyncingControls = true;
				PresetCombo.SelectedIndex = index;
				isSyncingControls = false;
			}
		}

		private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			DeletePresetButton.IsEnabled = PresetCombo.SelectedIndex >= 0;
			if (isSyncingControls || vm is null || PresetCombo.SelectedIndex < 0 || PresetCombo.SelectedIndex >= presets.Count)
				return;

			ApplyPreset(presets[PresetCombo.SelectedIndex]);
		}

		private void ApplyPreset(RenamePreset preset)
		{
			if (vm is null)
				return;

			isSyncingControls = true;
			vm.Rule = preset.Rule;
			vm.Scope = preset.Scope;
			vm.Depth = preset.Depth;
			vm.IncludeFiles = preset.IncludeFiles;
			vm.IncludeFolders = preset.IncludeFolders;
			isSyncingControls = false;

			SyncControlsFromVm();
			Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService.LastPresetName = preset.Name;
		}

		private async void SavePresetButton_Click(object sender, RoutedEventArgs e)
		{
			if (vm is null)
				return;

			TextBox input = new()
			{
				Header = Strings.BatchRenameSavePresetPrompt.GetLocalizedResource(),
			};

			ContentDialog dialog = new()
			{
				Title = Strings.BatchRenameSavePresetTitle.GetLocalizedResource(),
				Content = input,
				PrimaryButtonText = Strings.Save.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource(),
				XamlRoot = XamlRoot,
			};

			if (await dialog.ShowAsync() != ContentDialogResult.Primary || input.Text.Trim().Length == 0)
				return;

			string name = input.Text.Trim();
			RenamePreset preset = new(name, vm.Rule, vm.Scope, vm.Depth, vm.IncludeFiles, vm.IncludeFolders);
			int existing = presets.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (existing >= 0)
				presets[existing] = preset;
			else
				presets.Add(preset);

			await SavePresetsAsync();
			RefreshPresetCombo(name);
			Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService.LastPresetName = name;
		}

		private async void DeletePresetButton_Click(object sender, RoutedEventArgs e)
		{
			if (PresetCombo.SelectedIndex < 0 || PresetCombo.SelectedIndex >= presets.Count)
				return;

			string name = presets[PresetCombo.SelectedIndex].Name;
			presets.RemoveAt(PresetCombo.SelectedIndex);
			await SavePresetsAsync();
			RefreshPresetCombo(null);

			var settings = Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService;
			if (string.Equals(settings.LastPresetName, name, StringComparison.OrdinalIgnoreCase))
				settings.LastPresetName = string.Empty;
		}

		private async Task SavePresetsAsync()
		{
			try
			{
				await presetStore.SaveAsync(presets);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "The rename presets could not be saved.");
			}
		}

		private void RefreshPresetCombo(string? selectName)
		{
			isSyncingControls = true;
			PresetCombo.Items.Clear();
			foreach (RenamePreset preset in presets)
				PresetCombo.Items.Add(preset.Name);

			int index = selectName is null ? -1 : presets.FindIndex(p => string.Equals(p.Name, selectName, StringComparison.OrdinalIgnoreCase));
			PresetCombo.SelectedIndex = index;
			DeletePresetButton.IsEnabled = index >= 0;
			isSyncingControls = false;
		}

		// Strategy / filter / window chrome

		private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && StrategyCombo.SelectedIndex >= 0)
				vm.ConflictStrategy = (ConflictStrategy)StrategyCombo.SelectedIndex;
		}

		private void FilterSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!isSyncingControls && vm is not null && FilterSegmented.SelectedIndex >= 0)
				vm.PreviewFilter = (PreviewFilter)FilterSegmented.SelectedIndex;
		}

		private void DockButton_Click(object sender, RoutedEventArgs e)
		{
			bool dockedLeft = !ApplyDockState(null);
			ApplyDockState(dockedLeft);
			Ioc.Default.GetRequiredService<IUserSettingsService>().BatchRenameSettingsService.RulePanelDockedLeft = dockedLeft;
		}

		/// <summary>Applies a dock state; passing null keeps the current one. Returns the applied state.</summary>
		private bool ApplyDockState(bool? dockedLeft)
		{
			bool effective = dockedLeft ?? VisualStateManager.GetVisualStateGroups(RootGrid)
				.First(group => group.Name == "DockStates").CurrentState?.Name == "DockLeftState";
			VisualStateManager.GoToState(this, effective ? "DockLeftState" : "DockRightState", true);
			DockIcon.Glyph = effective ? "\uE90C" : "\uE90D";
			DockLabel.Text = (effective ? Strings.BatchRenameDockLeftLabel : Strings.BatchRenameDockRightLabel).GetLocalizedResource();
			return effective;
		}

		private void MaximizeButton_Click(object sender, RoutedEventArgs e)
		{
			bool maximized = VisualStateManager.GetVisualStateGroups(RootGrid)
				.First(group => group.Name == "SizeStates").CurrentState?.Name == "MaximizedState";

			VisualStateManager.GoToState(this, maximized ? "NormalSizeState" : "MaximizedState", true);
			MaximizeButton.Content = maximized ? "\uE922" : "\uE923";
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		// Apply

		private async void ApplyButton_Click(object sender, RoutedEventArgs e)
		{
			await ApplyAsync();
		}

		private async Task ApplyAsync()
		{
			if (vm is null || isApplying || !vm.ApplyCommand.CanExecute(null))
				return;

			// Close() nulls the session state, so the refresh below must use the page captured here.
			IShellPage? page = shellPage;

			if (vm.ConflictStrategy == ConflictStrategy.Overwrite && vm.Counts.Overwrite > 0 && !await ConfirmOverwriteAsync())
				return;

			isApplying = true;
			ProgressText.Text = Strings.BatchRenameApplying.GetLocalizedResource();
			UpdateApplyUi();

			List<(string Path, string Error)> failed = [];
			bool canceled = false;
			try
			{
				await vm.ApplyCommand.ExecuteAsync(null);
				if (executor?.LastResult is { } result)
					failed = [.. result.Failed];
			}
			catch (OperationCanceledException)
			{
				canceled = true;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "The batch rename failed.");
				failed.Add((string.Empty, ex.Message));
			}

			isApplying = false;

			if (IsOpen)
			{
				UpdateApplyUi();
				if (canceled)
					SetNotice(Strings.BatchRenameCanceled.GetLocalizedResource());
			}

			if (!canceled && failed.Count > 0)
				await ShowFailuresAsync(failed);

			if (canceled)
			{
				// Partial renames may have landed; refresh behind the overlay but let the user continue.
				if (page is not null)
					await page.Refresh_Click();
			}
			else
			{
				Close();
				if (page is not null)
					await page.Refresh_Click();
			}
		}

		private void ReportApplyProgress(string message)
		{
			if (isApplying)
				ProgressText.Text = message;
		}

		private async Task<bool> ConfirmOverwriteAsync()
		{
			if (vm is null)
				return false;

			ContentDialog dialog = new()
			{
				Title = Strings.BatchRenameOverwriteConfirmTitle.GetLocalizedResource(),
				Content = string.Format(Strings.BatchRenameOverwriteConfirmMessage.GetLocalizedResource(), vm.Counts.Overwrite),
				PrimaryButtonText = Strings.BatchRenameOverwriteConfirmButton.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource(),
				XamlRoot = XamlRoot,
			};

			return await dialog.ShowAsync() == ContentDialogResult.Primary;
		}

		private async Task ShowFailuresAsync(IReadOnlyList<(string Path, string Error)> failed)
		{
			ListView list = new()
			{
				MaxHeight = 320,
				SelectionMode = ListViewSelectionMode.None,
				ItemsSource = failed.Select(failure => $"{failure.Path} — {failure.Error}").ToArray(),
			};

			ContentDialog dialog = new()
			{
				Title = Strings.BatchRenameFailuresTitle.GetLocalizedResource(),
				Content = list,
				CloseButtonText = Strings.Close.GetLocalizedResource(),
				XamlRoot = XamlRoot,
			};

			await dialog.ShowAsync();
		}

		/// <summary>
		/// Feeds the executor's per-item progress into the overlay and keeps the last execution result:
		/// the view model forwards a null progress to the executor and drops its return value, while the
		/// overlay is the one that constructs the executor it injects.
		/// </summary>
		private sealed class UiRenameExecutor(RenameExecutor inner, Func<IProgress<string>?> progressSource) : IRenameExecutor
		{
			/// <summary>The result of the last completed execution; null when it never finished.</summary>
			public RenameExecutionResult? LastResult { get; private set; }

			public async Task<RenameExecutionResult> ExecuteAsync(IReadOnlyList<RenameExecutionItem> plan, IFilesystemHelpers fs, IProgress<string>? progress, CancellationToken ct)
			{
				RenameExecutionResult result = await inner.ExecuteAsync(plan, fs, progressSource() ?? progress, ct);
				LastResult = result;
				return result;
			}
		}
	}
}
