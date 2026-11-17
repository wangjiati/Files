// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Files.App.ViewModels.UserControls
{
	public enum FilterMode
	{
		Basic,
		Advanced,
	}

	/// <summary>
	/// Coordinates the filter bar: the basic keyword box and the advanced condition chain are separate
	/// configurations, and only the active mode is projected into a <see cref="DirectoryFilterSpec"/>.
	/// </summary>
	public sealed partial class DirectoryFilterBarViewModel : ObservableObject
	{
		private readonly HashSet<DirectoryFilterViewModel> trackedConditions = [];

		private DirectoryFilterViewModel nameCondition = new(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
		private int recursiveDepth = 2;
		private bool suppressSpecChanged;

		[ObservableProperty]
		private FilterMode mode;

		[ObservableProperty]
		private string? keywordText;

		[ObservableProperty]
		private bool recursiveEnabled;

		public DirectoryFilterBarViewModel()
		{
			nameCondition.PropertyChanged += OnConditionPropertyChanged;
			Conditions.CollectionChanged += OnConditionsChanged;
		}

		public event EventHandler? SpecChanged;

		/// <summary>
		/// Raised when Enter confirms a filter edit; the shell re-runs the projection even when the
		/// debounced text change already produced the same spec.
		/// </summary>
		public event EventHandler? CommitRequested;

		/// <summary>
		/// Confirms the current editor state, for example when Enter is pressed in a filter text box.
		/// </summary>
		public void ApplyNow()
			=> CommitRequested?.Invoke(this, EventArgs.Empty);

		public ObservableCollection<DirectoryFilterViewModel> Conditions { get; } = [];

		/// <summary>
		/// Name condition edited above the condition chain. It is separate from <see cref="Conditions"/> so it
		/// stays available in advanced mode, while the add menu can still create further name conditions.
		/// </summary>
		public DirectoryFilterViewModel NameCondition
		{
			get => nameCondition;
			private set
			{
				if (ReferenceEquals(nameCondition, value))
					return;

				nameCondition.PropertyChanged -= OnConditionPropertyChanged;
				nameCondition = value;
				nameCondition.PropertyChanged += OnConditionPropertyChanged;
				OnPropertyChanged();
				OnPropertyChanged(nameof(HasActiveFilter));
				OnPropertyChanged(nameof(ShowNameCondition));
				UpdateConditionJoins();
				RaiseSpecChanged();
			}
		}

		/// <summary>
		/// Values for choice columns created through <see cref="AddCondition"/>. The shell wires this to its
		/// filter choice source so column headers and the add menu both get selectable values.
		/// </summary>
		public Func<DirectoryColumnKind, IReadOnlyList<DirectoryFilterChoice>>? ChoiceProvider { get; set; }

		public bool IsBasicMode => Mode is FilterMode.Basic;

		public bool IsAdvancedMode => Mode is FilterMode.Advanced;

		/// <summary>
		/// The inline name editor stays out of the way once the condition list provides the filter UI.
		/// It remains visible when it holds the active name filter or when the advanced row is empty.
		/// </summary>
		public bool ShowNameCondition
			=> IsAdvancedMode && (NameCondition.IsActive || Conditions.Count == 0);

		/// <summary>
		/// Whether the active configuration currently contributes to the projected spec. Basic mode only
		/// filters when the keyword box has text, while advanced mode uses the condition editors.
		/// </summary>
		public bool HasActiveFilter
			=> Mode is FilterMode.Basic
				? !string.IsNullOrWhiteSpace(KeywordText)
				: NameCondition.IsActive || Conditions.Any(condition => condition.IsActive);

		/// <summary>
		/// Recursion depth, clamped to 1..64 where 64 is treated as unlimited by consumers.
		/// </summary>
		public int RecursiveDepth
		{
			get => recursiveDepth;
			set
			{
				if (SetProperty(ref recursiveDepth, Math.Clamp(value, 1, 64)))
					RaiseSpecChanged();
			}
		}

		public DirectoryFilterSpec BuildSpec()
		{
			if (Mode is FilterMode.Basic)
				return DirectoryFilterSpec.FromKeywordText(KeywordText);

			var columns = new List<DirectoryColumnFilter>();

			if (NameCondition.IsActive)
				columns.Add(NameCondition.ToColumnFilter());

			columns.AddRange(Conditions
				.Where(condition => condition.IsActive)
				.Select(condition => condition.ToColumnFilter()));

			return columns.Count is 0
				? DirectoryFilterSpec.Empty
				: new DirectoryFilterSpec { Columns = columns };
		}

		public DirectoryFilterViewModel AddCondition(DirectoryColumnKind column, DirectoryConditionJoin join)
		{
			var condition = new DirectoryFilterViewModel(column, DirectoryFilterSpec.Empty)
			{
				Join = join,
			};

			if (ChoiceProvider is { } provider
				&& column is (DirectoryColumnKind.Type or DirectoryColumnKind.Extension or DirectoryColumnKind.Tags))
			{
				var choices = provider(column);

				if (column is DirectoryColumnKind.Extension)
					condition.LoadChoices(extensions: choices);
				else
					condition.LoadChoices(choices);
			}

			Conditions.Add(condition);
			return condition;
		}

		/// <summary>
		/// Removes every condition for one column. Used by the column filter popup's clear action.
		/// </summary>
		public void RemoveColumnConditions(DirectoryColumnKind column)
		{
			foreach (var condition in Conditions.Where(item => item.Column == column).ToList())
				Conditions.Remove(condition);
		}

		/// <summary>
		/// Resets the inline name condition; used by the name column header's clear action.
		/// </summary>
		public void ClearNameCondition()
			=> NameCondition = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);

		public bool RemoveCondition(DirectoryFilterViewModel condition)
			=> Conditions.Remove(condition);

		public void ClearAll()
		{
			var wasSuppressed = suppressSpecChanged;
			suppressSpecChanged = true;

			try
			{
				KeywordText = null;
				Conditions.Clear();
				NameCondition = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			}
			finally
			{
				suppressSpecChanged = wasSuppressed;
			}

			if (!wasSuppressed)
				RaiseSpecChanged();
		}

		/// <summary>
		/// Restores the bar from a spec set outside the bar, such as a navigation restore or a column
		/// filter. The bar must not project the restored spec back, so <see cref="SpecChanged"/> stays
		/// silent and the caller remains the owner of this projection.
		/// </summary>
		public void LoadFrom(DirectoryFilterSpec spec)
		{
			spec ??= DirectoryFilterSpec.Empty;

			if (Represents(spec))
				return;

			var wasSuppressed = suppressSpecChanged;
			suppressSpecChanged = true;

			try
			{
				if (spec.IsEmpty)
				{
					ClearAll();
					return;
				}

				if (Mode is FilterMode.Basic && TryGetKeywordText(spec, out var keywordText))
				{
					KeywordText = keywordText;
					return;
				}

				LoadAdvanced(spec);
			}
			finally
			{
				suppressSpecChanged = wasSuppressed;
			}
		}

		private bool Represents(DirectoryFilterSpec spec)
			=> SpecContentEquals(BuildSpec(), spec);

		private static bool TryGetKeywordText(DirectoryFilterSpec spec, out string keywordText)
		{
			keywordText = string.Empty;

			if (spec.Regex is not null)
				return false;

			var texts = new List<string>();

			if (!string.IsNullOrEmpty(spec.NameContains))
			{
				if (!IsKeywordText(spec.NameContains, DirectoryTextOperator.Contains))
					return false;

				texts.Add(spec.NameContains);
			}

			foreach (var column in spec.Columns)
			{
				if (column.Column is not DirectoryColumnKind.Name
					|| column.Regex is not null
					|| column.Length is not null
					|| column.CaseSensitive
					|| !IsKeywordScope(column)
					|| !IsKeywordText(column.Text, column.Operator))
				{
					return false;
				}

				texts.Add(column.Text!);
			}

			if (texts.Count is 0)
				return false;

			keywordText = string.Join(" ", texts);
			return true;
		}

		// Keyword tokens match the raw name so the keyword box also matches the extension.
		private static bool IsKeywordScope(DirectoryColumnFilter column)
			=> column.Scope is DirectoryNameScope.RawName;

		// The keyword parser splits on whitespace and commas and treats '*' and '?' as wildcards, so only
		// tokens that survive that parse as the same condition can be restored into the keyword box.
		private static bool IsKeywordText(string? text, DirectoryTextOperator op)
		{
			if (string.IsNullOrWhiteSpace(text)
				|| text.Any(char.IsWhiteSpace)
				|| text.Contains(',')
				|| text.Contains('，'))
			{
				return false;
			}

			var hasWildcard = text.IndexOfAny(['*', '?']) >= 0;

			return op switch
			{
				DirectoryTextOperator.Contains => !hasWildcard,
				DirectoryTextOperator.Wildcard => hasWildcard,
				_ => false,
			};
		}

		private void LoadAdvanced(DirectoryFilterSpec spec)
		{
			Mode = FilterMode.Advanced;

			var columns = spec.Columns.ToList();

			// The evaluator applies NameContains before the column chain; keeping it as the leading name
			// condition preserves that AND semantics for restored address filters.
			if (!string.IsNullOrEmpty(spec.NameContains))
			{
				columns.Insert(0, new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Operator = DirectoryTextOperator.Contains,
					Text = spec.NameContains,
				});
			}

			var nameIndex = columns.FindIndex(column => column.Column is DirectoryColumnKind.Name);
			var nameColumn = nameIndex >= 0 ? columns[nameIndex] : null;

			if (nameIndex >= 0)
				columns.RemoveAt(nameIndex);

			var nameCondition = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);

			if (nameColumn is not null)
				nameCondition.LoadFrom(nameColumn);

			if (spec.Regex is { } regex)
			{
				nameCondition.UseRegex = true;
				nameCondition.RegexPattern = regex.Pattern;
				nameCondition.RegexTarget = regex.Target;
				nameCondition.RegexIgnoreCase = regex.IgnoreCase;
			}

			NameCondition = nameCondition;
			Conditions.Clear();

			foreach (var column in columns)
			{
				var condition = new DirectoryFilterViewModel(column.Column, DirectoryFilterSpec.Empty);
				condition.LoadFrom(column);
				LoadConditionChoices(condition, column);
				Conditions.Add(condition);
			}
		}

		private void LoadConditionChoices(DirectoryFilterViewModel condition, DirectoryColumnFilter column)
		{
			if (column.Column is not (DirectoryColumnKind.Type or DirectoryColumnKind.Extension or DirectoryColumnKind.Tags))
				return;

			var choices = ChoiceProvider is { } provider
				? provider(column.Column).ToList()
				: [];

			// Keep restored values selectable even when the current directory no longer offers them;
			// otherwise the condition would turn inactive and the next bar edit would drop it.
			foreach (var value in column.Values)
			{
				if (!choices.Any(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)))
					choices.Add(new DirectoryFilterChoice(value, value));
			}

			if (column.Column is DirectoryColumnKind.Extension)
				condition.LoadChoices(extensions: choices);
			else
				condition.LoadChoices(choices);
		}

		private static bool SpecContentEquals(DirectoryFilterSpec left, DirectoryFilterSpec right)
		{
			if (!string.Equals(left.NameContains, right.NameContains, StringComparison.Ordinal)
				|| !Equals(left.Regex, right.Regex)
				|| left.Columns.Count != right.Columns.Count)
			{
				return false;
			}

			for (var index = 0; index < left.Columns.Count; index++)
			{
				if (!ColumnContentEquals(left.Columns[index], right.Columns[index]))
					return false;
			}

			return true;
		}

		private static bool ColumnContentEquals(DirectoryColumnFilter left, DirectoryColumnFilter right)
			=> left.Join == right.Join
				&& left.Column == right.Column
				&& left.Operator == right.Operator
				&& left.Scope == right.Scope
				&& left.CaseSensitive == right.CaseSensitive
				&& Equals(left.Regex, right.Regex)
				&& string.Equals(left.Text, right.Text, StringComparison.Ordinal)
				&& left.Length == right.Length
				&& left.Values.SequenceEqual(right.Values, StringComparer.Ordinal)
				&& left.MinSize == right.MinSize
				&& left.MaxSize == right.MaxSize
				&& left.MinDate == right.MinDate
				&& left.MaxDate == right.MaxDate
				&& left.IncludeEmpty == right.IncludeEmpty;

		partial void OnModeChanged(FilterMode value)
		{
			OnPropertyChanged(nameof(IsBasicMode));
			OnPropertyChanged(nameof(IsAdvancedMode));
			OnPropertyChanged(nameof(ShowNameCondition));
			OnPropertyChanged(nameof(HasActiveFilter));
			RaiseSpecChanged();
		}

		partial void OnKeywordTextChanged(string? value)
		{
			OnPropertyChanged(nameof(HasActiveFilter));
			RaiseSpecChanged();
		}

		partial void OnRecursiveEnabledChanged(bool value) => RaiseSpecChanged();

		private void OnConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action is NotifyCollectionChangedAction.Reset)
			{
				foreach (var condition in trackedConditions)
					condition.PropertyChanged -= OnConditionPropertyChanged;

				trackedConditions.Clear();

				foreach (var condition in Conditions)
					TrackCondition(condition);
			}
			else
			{
				if (e.OldItems is not null)
				{
					foreach (DirectoryFilterViewModel condition in e.OldItems)
						UntrackCondition(condition);
				}

				if (e.NewItems is not null)
				{
					foreach (DirectoryFilterViewModel condition in e.NewItems)
						TrackCondition(condition);
				}
			}

			OnPropertyChanged(nameof(HasActiveFilter));
			OnPropertyChanged(nameof(ShowNameCondition));
			UpdateConditionJoins();
			RaiseSpecChanged();
		}

		private void TrackCondition(DirectoryFilterViewModel condition)
		{
			if (trackedConditions.Add(condition))
				condition.PropertyChanged += OnConditionPropertyChanged;
		}

		private void UntrackCondition(DirectoryFilterViewModel condition)
		{
			if (trackedConditions.Remove(condition))
				condition.PropertyChanged -= OnConditionPropertyChanged;
		}

		private void OnConditionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			// Join visibility is UI state, not spec content; do not re-project for it.
			if (e.PropertyName is nameof(DirectoryFilterViewModel.ShowsJoin))
				return;

			OnPropertyChanged(nameof(HasActiveFilter));
			OnPropertyChanged(nameof(ShowNameCondition));
			UpdateConditionJoins();
			RaiseSpecChanged();
		}

		/// <summary>
		/// Hides the connector of the first active condition; the name editor counts as the first
		/// only while it holds an active condition.
		/// </summary>
		private void UpdateConditionJoins()
		{
			var hasPrecedingCondition = NameCondition.IsActive;

			foreach (var condition in Conditions)
			{
				condition.ShowsJoin = hasPrecedingCondition;
				hasPrecedingCondition |= condition.IsActive;
			}
		}

		private void RaiseSpecChanged()
		{
			if (!suppressSpecChanged)
				SpecChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
