// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Helpers
{
	internal sealed class PortableLayoutPreferencesDatabase : ILayoutPreferencesDatabase
	{
		private readonly string _filePath;
		private readonly object _syncRoot = new();
		private List<LayoutPreferencesDatabaseItem> _items;

		public PortableLayoutPreferencesDatabase(string filePath)
		{
			_filePath = Path.GetFullPath(filePath);
			var items = VersionedJsonFile.Read(_filePath, "items");
			_items = items is null
				? []
				: JsonSerializer.Deserialize(items.ToJsonString(), AppJsonSerializerContext.Default.ListLayoutPreferencesDatabaseItem) ?? [];
		}

		public LayoutPreferencesItem? GetPreferences(string filePath, ulong? frn = null)
		{
			lock (_syncRoot)
			{
				var index = FindIndex(filePath, frn);
				if (index < 0)
					return null;

				var item = _items[index];
				if ((frn is not null && item.Frn != frn) || !PathsEqual(item.FilePath, filePath))
				{
					var updated = new LayoutPreferencesDatabaseItem
					{
						FilePath = filePath,
						Frn = frn ?? item.Frn,
						LayoutPreferencesManager = item.LayoutPreferencesManager,
					};
					var items = _items.ToArray();
					items[index] = updated;
					Persist(items.ToList());
				}

				return _items[index].LayoutPreferencesManager;
			}
		}

		public void SetPreferences(string filePath, ulong? frn, LayoutPreferencesItem? preferencesItem)
		{
			lock (_syncRoot)
			{
				var index = FindIndex(filePath, frn);
				var updated = _items.Where((_, itemIndex) => itemIndex != index).ToList();
				if (preferencesItem is not null)
				{
					updated.Add(new()
					{
						FilePath = filePath,
						Frn = frn ?? (index >= 0 ? _items[index].Frn : null),
						LayoutPreferencesManager = preferencesItem,
					});
				}
				Persist(updated);
			}
		}

		public void ResetAll()
		{
			lock (_syncRoot)
				Persist([]);
		}

		public void Import(string json)
		{
			var imported = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.LayoutPreferencesDatabaseItemArray)?.ToList() ?? [];
			lock (_syncRoot)
				Persist(imported);
		}

		public string Export()
		{
			lock (_syncRoot)
				return JsonSerializer.Serialize(_items, AppJsonSerializerContext.Default.ListLayoutPreferencesDatabaseItem);
		}

		private int FindIndex(string filePath, ulong? frn)
		{
			var index = _items.FindIndex(item => PathsEqual(item.FilePath, filePath));
			if (index >= 0 || frn is null)
				return index;

			return _items.FindIndex(item => item.Frn == frn);
		}

		private void Persist(List<LayoutPreferencesDatabaseItem> items)
		{
			var json = JsonSerializer.SerializeToNode(items, AppJsonSerializerContext.Default.ListLayoutPreferencesDatabaseItem);
			VersionedJsonFile.Write(_filePath, "items", json);
			_items = items;
		}

		private static bool PathsEqual(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
	}
}
