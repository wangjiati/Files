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

		public ObservableCollection<DirectoryFilterViewModel> Conditions { get; } = [];

		/// <summary>
		/// Name condition edited above the condition chain. It is separate so it is always available
		/// in advanced mode and never duplicated by the add menu.
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
				RaiseSpecChanged();
			}
		}

		public bool IsBasicMode => Mode is FilterMode.Basic;

		public bool IsAdvancedMode => Mode is FilterMode.Advanced;

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

		public void AddCondition(DirectoryColumnKind column, DirectoryConditionJoin join)
		{
			Conditions.Add(new DirectoryFilterViewModel(column, DirectoryFilterSpec.Empty)
			{
				Join = join,
			});
		}

		public bool RemoveCondition(DirectoryFilterViewModel condition)
			=> Conditions.Remove(condition);

		public void ClearAll()
		{
			suppressSpecChanged = true;

			try
			{
				KeywordText = null;
				Conditions.Clear();
				NameCondition = new DirectoryFilterViewModel(DirectoryColumnKind.Name, DirectoryFilterSpec.Empty);
			}
			finally
			{
				suppressSpecChanged = false;
			}

			RaiseSpecChanged();
		}

		partial void OnModeChanged(FilterMode value)
		{
			OnPropertyChanged(nameof(IsBasicMode));
			OnPropertyChanged(nameof(IsAdvancedMode));
			RaiseSpecChanged();
		}

		partial void OnKeywordTextChanged(string? value) => RaiseSpecChanged();

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

		private void OnConditionPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseSpecChanged();

		private void RaiseSpecChanged()
		{
			if (!suppressSpecChanged)
				SpecChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
