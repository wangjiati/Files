// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.UserControls.Selection
{
	public abstract class ItemSelectionStrategy
	{
		protected readonly ICollection<object> selectedItems;

		// Mutations applied since the last ResetAppliedChanges call. Consumers batch
		// these into a single SelectionChanged instead of diffing the whole selection.
		// Bulk clears (StartSelection/HandleNoItemSelected) are not recorded: they run
		// outside the move loop and reach handlers through the control's own event.
		private readonly List<object> appliedAddedItems = [];
		private readonly List<object> appliedRemovedItems = [];

		protected ItemSelectionStrategy(ICollection<object> selectedItems)
		{
			this.selectedItems = selectedItems;
		}

		public abstract void HandleIntersectionWithItem(object item);

		public abstract void HandleNoIntersectionWithItem(object item);

		public virtual void StartSelection()
		{
		}

		public virtual void HandleNoItemSelected()
		{
		}

		public IReadOnlyList<object> AppliedAddedItems => appliedAddedItems;

		public IReadOnlyList<object> AppliedRemovedItems => appliedRemovedItems;

		public void ResetAppliedChanges()
		{
			appliedAddedItems.Clear();
			appliedRemovedItems.Clear();
		}

		protected void RecordItemAdded(object item)
		{
			appliedAddedItems.Add(item);
		}

		protected void RecordItemRemoved(object item)
		{
			appliedRemovedItems.Add(item);
		}
	}
}
