// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.UserControls.Selection
{
	/// <summary>
	/// Maintains the set of items intersecting the selection rectangle across
	/// pointer moves. The strategies are idempotent for items whose intersection
	/// state did not change, so only those items get a strategy call per move.
	/// </summary>
	internal static class SelectionIntersectionTracker
	{
		public static void ApplyIntersectionChanges(
			Dictionary<object, System.Drawing.Rectangle> itemsPosition,
			System.Drawing.Rectangle selectionRectangle,
			HashSet<object> intersectingItems,
			Action<object> handleIntersection,
			Action<object> handleNoIntersection)
		{
			// snapshot: stale items are removed from the map during iteration
			foreach (var item in itemsPosition.ToList())
			{
				var isIntersecting = selectionRectangle.IntersectsWith(item.Value);
				if (isIntersecting == intersectingItems.Contains(item.Key))
					continue;

				try
				{
					if (isIntersecting)
					{
						handleIntersection(item.Key);
						intersectingItems.Add(item.Key);
					}
					else
					{
						handleNoIntersection(item.Key);
						intersectingItems.Remove(item.Key);
					}
				}
				catch (ArgumentException)
				{
					// Item is not present in the ItemsSource
					itemsPosition.Remove(item.Key);
					intersectingItems.Remove(item.Key);
				}
			}
		}
	}
}
