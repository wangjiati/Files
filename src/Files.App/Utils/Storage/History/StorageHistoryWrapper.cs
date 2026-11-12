// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	public sealed partial class StorageHistoryWrapper : IDisposable
	{
		/// <summary>
		/// Maximum number of history entries retained; adding more discards the oldest ones.
		/// </summary>
		public const int MaxHistoryCount = 100;

		private int index = -1;

		private List<IStorageHistory> histories = [];

		public bool CanRedo() => index + 1 < histories.Count;
		public bool CanUndo() => index >= 0 && histories.Count > 0;

		public IStorageHistory GetCurrentHistory() => histories[index];

		public void AddHistory(IStorageHistory? history)
		{
			if (history is not null)
			{
				++index;
				histories.Insert(index, history);
				histories.RemoveRange(index + 1, histories.Count - index - 1);

				if (histories.Count > MaxHistoryCount)
				{
					// Discard the oldest entries and shift index along so it keeps pointing at the current history.
					var discardCount = histories.Count - MaxHistoryCount;
					histories.RemoveRange(0, discardCount);
					index -= discardCount;
				}
			}
		}

		public void RemoveHistory(IStorageHistory history, bool decreaseIndex = true)
		{
			if (history is not null)
			{
				histories.RemoveRange(index + 1, histories.Count - index - 1);
				if (decreaseIndex)
				{
					--index;
				}
				histories.Remove(history);
			}
		}

		public void ModifyCurrentHistory(IStorageHistory newHistory)
			=> histories[index].Modify(newHistory);

		public void DecreaseIndex() => --index;
		public void IncreaseIndex() => ++index;

		public void Dispose()
		{
			histories.Clear();
			index = -1;
		}
	}
}
