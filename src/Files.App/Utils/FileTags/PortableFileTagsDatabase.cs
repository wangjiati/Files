// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Files.App.Utils.FileTags
{
	internal sealed class PortableFileTagsDatabase : IFileTagsDatabase
	{
		private readonly string _filePath;
		private readonly object _syncRoot = new();
		private List<TaggedFile> _items;

		public PortableFileTagsDatabase(string filePath)
		{
			_filePath = Path.GetFullPath(filePath);
			try
			{
				var items = VersionedJsonFile.Read(_filePath, "items");
				_items = items is null
					? []
					: JsonSerializer.Deserialize(items.ToJsonString(), AppJsonSerializerContext.Default.ListTaggedFile) ?? [];
			}
			catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
			{
				VersionedJsonFile.PreserveUnreadableFile(_filePath, ex);
				_items = [];
			}
		}

		public void SetTags(string filePath, ulong? frn, string[] tags)
		{
			lock (_syncRoot)
			{
				var updated = _items.Where(item => !Matches(item, filePath, frn)).ToList();
				if (tags.Length > 0)
				{
					updated.Add(new()
					{
						FilePath = filePath,
						Frn = frn,
						Tags = tags,
					});
				}
				Persist(updated);
			}
		}

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath)
		{
			lock (_syncRoot)
			{
				var index = FindIndex(oldFilePath, null);
				if (index >= 0)
					UpdateAt(index, frn, newFilePath);
			}
		}

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath)
		{
			lock (_syncRoot)
			{
				var index = FindIndex(null, oldFrn);
				if (index >= 0)
					UpdateAt(index, frn, newFilePath);
			}
		}

		public string[] GetTags(string? filePath, ulong? frn)
		{
			lock (_syncRoot)
			{
				var index = FindIndex(filePath, frn);
				if (index < 0)
					return [];

				var item = _items[index];
				if ((frn is not null && item.Frn != frn) || (filePath is not null && !PathsEqual(item.FilePath, filePath)))
					return UpdateAt(index, frn, filePath).Tags.ToArray();

				return item.Tags.ToArray();
			}
		}

		public IEnumerable<TaggedFile> GetAll()
		{
			lock (_syncRoot)
				return _items.Select(Copy).ToArray();
		}

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath)
		{
			folderPath = folderPath.Replace('/', '\\').TrimEnd('\\');
			var prefix = $"{folderPath}\\";
			lock (_syncRoot)
			{
				return _items
					.Where(item => PathsEqual(item.FilePath, folderPath) || item.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					.Select(Copy)
					.ToArray();
			}
		}

		public void Import(string json)
		{
			var imported = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.TaggedFileArray)?.ToList() ?? [];
			lock (_syncRoot)
				Persist(imported);
		}

		public string Export()
		{
			lock (_syncRoot)
				return JsonSerializer.Serialize(_items, AppJsonSerializerContext.Default.ListTaggedFile);
		}

		private int FindIndex(string? filePath, ulong? frn)
		{
			if (filePath is not null)
			{
				var index = _items.FindIndex(item => PathsEqual(item.FilePath, filePath));
				if (index >= 0)
					return index;
			}

			return frn is null ? -1 : _items.FindIndex(item => item.Frn == frn);
		}

		private TaggedFile UpdateAt(int index, ulong? frn, string? newFilePath)
		{
			var current = _items[index];
			var updatedItem = new TaggedFile
			{
				FilePath = newFilePath ?? current.FilePath,
				Frn = frn ?? current.Frn,
				Tags = current.Tags,
			};
			var updated = _items.Where((item, itemIndex) => itemIndex != index && !Matches(item, updatedItem.FilePath, updatedItem.Frn)).ToList();
			updated.Add(updatedItem);
			Persist(updated);
			return updatedItem;
		}

		private void Persist(List<TaggedFile> items)
		{
			var json = JsonSerializer.SerializeToNode(items, AppJsonSerializerContext.Default.ListTaggedFile);
			VersionedJsonFile.Write(_filePath, "items", json);
			_items = items;
		}

		private static bool Matches(TaggedFile item, string filePath, ulong? frn) =>
			PathsEqual(item.FilePath, filePath) || (frn is not null && item.Frn == frn);

		private static bool PathsEqual(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

		private static TaggedFile Copy(TaggedFile item) => new()
		{
			FilePath = item.FilePath,
			Frn = item.Frn,
			Tags = item.Tags.ToArray(),
		};
	}
}
