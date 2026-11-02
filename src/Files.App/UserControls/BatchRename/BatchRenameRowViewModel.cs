// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Files.App.UserControls
{
	/// <summary>
	/// UI-facing wrapper of one <see cref="RenamePreviewRow"/>: binds the row template, renders the
	/// name diff and the status tag, and carries the transient in-place editing state.
	/// </summary>
	/// <remarks>
	/// The overlay recreates every wrapper whenever the view model replaces its rows, so the wrapped
	/// values are read once per rebuild instead of being observable. Only the state the overlay mutates
	/// directly (editing, per-row inclusion) raises change notifications.
	/// </remarks>
	internal sealed partial class BatchRenameRowViewModel : ObservableObject
	{
		/// <summary>Notifies the overlay that a per-row flag changed and derived UI must refresh.</summary>
		private readonly Action onRowStateChanged;

		public RenamePreviewRow Source { get; }

		public string GroupHeader { get; }

		public string PositionText { get; }

		public bool ShowPosition { get; }

		// Name diff: common prefix/suffix trimmed, the middles are the removed/added segments.
		public string OldPrefix { get; }
		public string OldRemoved { get; }
		public string OldSuffix { get; }
		public string NewPrefix { get; }
		public string NewAdded { get; }
		public string NewSuffix { get; }
		public bool ShowDiff { get; }

		public BatchRenameRowViewModel(RenamePreviewRow source, string groupHeader, string positionText, bool showPosition, Action onRowStateChanged)
		{
			Source = source;
			GroupHeader = groupHeader;
			PositionText = positionText;
			ShowPosition = showPosition;
			this.onRowStateChanged = onRowStateChanged;

			string original = source.Target.Name;
			string final = source.FinalName;
			var (oldPrefix, oldRemoved, oldSuffix, newPrefix, newAdded, newSuffix) = SplitDiff(original, final);
			OldPrefix = oldPrefix;
			OldRemoved = oldRemoved;
			OldSuffix = oldSuffix;
			NewPrefix = newPrefix;
			NewAdded = newAdded;
			NewSuffix = newSuffix;

			ShowDiff =
				source.Status is RenameRowStatus.Changed or RenameRowStatus.Sequenced or RenameRowStatus.Overwrite &&
				(oldRemoved.Length > 0 || newAdded.Length > 0);
		}

		// Display values

		public string OriginalName => Source.Target.Name;

		public string NewNameText => Source.Status == RenameRowStatus.Unchanged
			? Strings.BatchRenameRowUnchanged.GetLocalizedResource()
			: Source.FinalName;

		public double RowOpacity => !Source.IsIncluded ? 0.55
			: Source.Status == RenameRowStatus.Unchanged ? 0.45
			: 1.0;

		public Brush NewNameBrush => !Source.IsIncluded || Source.Status == RenameRowStatus.Unchanged
			? BatchRenameRowStyle.MutedBrush
			: Source.Status switch
			{
				RenameRowStatus.Error => BatchRenameRowStyle.ErrorBrush,
				RenameRowStatus.Sequenced or RenameRowStatus.Manual => BatchRenameRowStyle.SequenceBrush,
				_ => BatchRenameRowStyle.ChangedBrush,
			};

		public string? TagText => !Source.IsIncluded ? Strings.BatchRenameRowUnchecked.GetLocalizedResource()
			: Source.Status switch
			{
				RenameRowStatus.Manual => Strings.BatchRenameRowManual.GetLocalizedResource(),
				RenameRowStatus.Sequenced => Strings.BatchRenameRowSequenced.GetLocalizedResource(),
				RenameRowStatus.Skipped => Strings.BatchRenameRowSkipped.GetLocalizedResource(),
				RenameRowStatus.Overwrite => Strings.BatchRenameRowOverwrite.GetLocalizedResource(),
				RenameRowStatus.Error => Source.RowError == RenameError.DuplicateName
					? Strings.BatchRenameRowDuplicateName.GetLocalizedResource()
					: Strings.BatchRenameRowError.GetLocalizedResource(),
				RenameRowStatus.Unchanged => Source.UnchangedReason == UnchangedReason.ExcludedFolderType
					? Strings.BatchRenameRowExcludedFolder.GetLocalizedResource()
					: Strings.BatchRenameRowNotMatched.GetLocalizedResource(),
				_ => null,
			};

		public Visibility TagVisibility => TagText is null ? Visibility.Collapsed : Visibility.Visible;

		public Brush TagForeground => Source.Status is RenameRowStatus.Error or RenameRowStatus.Overwrite
			? BatchRenameRowStyle.ErrorBrush
			: Source.Status == RenameRowStatus.Unchanged || !Source.IsIncluded
				? BatchRenameRowStyle.MutedBrush
				: BatchRenameRowStyle.SequenceBrush;

		public Brush TagBackground => Source.Status is RenameRowStatus.Error or RenameRowStatus.Overwrite
			? BatchRenameRowStyle.ErrorTagBrush
			: Source.Status == RenameRowStatus.Unchanged || !Source.IsIncluded
				? BatchRenameRowStyle.NeutralTagBrush
				: BatchRenameRowStyle.SequenceTagBrush;

		public Visibility ResetButtonVisibility => Source.ManualName is null ? Visibility.Collapsed : Visibility.Visible;

		// Editable state

		[ObservableProperty]
		public partial bool IsEditing { get; set; }

		[ObservableProperty]
		public partial string EditText { get; set; }

		partial void OnIsEditingChanged(bool value)
		{
			OnPropertyChanged(nameof(EditVisibility));
			OnPropertyChanged(nameof(DisplayVisibility));
		}

		public bool IsIncluded
		{
			get => Source.IsIncluded;
			set
			{
				if (Source.IsIncluded == value)
					return;

				Source.IsIncluded = value;
				onRowStateChanged();
				OnPropertyChanged(nameof(TagText));
				OnPropertyChanged(nameof(TagVisibility));
				OnPropertyChanged(nameof(RowOpacity));
				OnPropertyChanged(nameof(NewNameBrush));
				OnPropertyChanged(nameof(TagForeground));
				OnPropertyChanged(nameof(TagBackground));
			}
		}

		public Visibility PositionVisibility => ShowPosition ? Visibility.Visible : Visibility.Collapsed;

		public Visibility DiffVisibility => ShowDiff ? Visibility.Visible : Visibility.Collapsed;

		public Visibility OldPlainVisibility => ShowDiff ? Visibility.Collapsed : Visibility.Visible;

		public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

		public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;

		/// <summary>
		/// Trims the common prefix and suffix of both names; the middles are the removed and added segments.
		/// </summary>
		private static (string OldPrefix, string OldRemoved, string OldSuffix, string NewPrefix, string NewAdded, string NewSuffix) SplitDiff(string original, string final)
		{
			int prefix = 0;
			while (prefix < original.Length && prefix < final.Length && original[prefix] == final[prefix])
				prefix++;

			int suffix = 0;
			while (
				suffix < original.Length - prefix &&
				suffix < final.Length - prefix &&
				original[original.Length - 1 - suffix] == final[final.Length - 1 - suffix])
			{
				suffix++;
			}

			return (
				original[..prefix],
				original[prefix..^suffix],
				suffix > 0 ? original[^suffix..] : string.Empty,
				final[..prefix],
				final[prefix..^suffix],
				suffix > 0 ? final[^suffix..] : string.Empty);
		}
	}

	/// <summary>
	/// Theme-aware brushes for the preview rows, resolved from the overlay's theme dictionaries whenever
	/// the theme changes or the rows are rebuilt.
	/// </summary>
	internal static class BatchRenameRowStyle
	{
		public static Brush? Changed { get; private set; }
		public static Brush? Error { get; private set; }
		public static Brush? Sequence { get; private set; }
		public static Brush? Muted { get; private set; }
		public static Brush? Added { get; private set; }
		public static Brush? ErrorTag { get; private set; }
		public static Brush? SequenceTag { get; private set; }
		public static Brush? NeutralTag { get; private set; }

		public static Brush ChangedBrush => Changed ??= (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
		public static Brush ErrorBrush => Error ??= (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
		public static Brush SequenceBrush => Sequence ??= (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
		public static Brush MutedBrush => Muted ??= (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
		public static Brush AddedBrush => Added ??= (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
		public static Brush ErrorTagBrush => ErrorTag ??= (Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"];
		public static Brush SequenceTagBrush => SequenceTag ??= (Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"];
		public static Brush NeutralTagBrush => NeutralTag ??= (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];

		/// <summary>
		/// Re-reads every brush from the given theme dictionary; call on load, theme change and before a rebuild.
		/// </summary>
		public static void Refresh(ResourceDictionary themeDictionary)
		{
			Changed = (Brush)themeDictionary["BatchRename.ChangedTextBrush"];
			Error = (Brush)themeDictionary["BatchRename.RemovedTextBrush"];
			Sequence = (Brush)themeDictionary["BatchRename.SequenceTextBrush"];
			Muted = (Brush)themeDictionary["BatchRename.MutedTextBrush"];
			Added = (Brush)themeDictionary["BatchRename.AddedTextBrush"];
			ErrorTag = (Brush)themeDictionary["BatchRename.ErrorTagBrush"];
			SequenceTag = (Brush)themeDictionary["BatchRename.SequenceTagBrush"];
			NeutralTag = (Brush)themeDictionary["BatchRename.NeutralTagBrush"];
		}
	}
}
