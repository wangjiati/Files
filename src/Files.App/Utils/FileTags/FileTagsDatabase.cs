// Copyright (c) Files Community
// Licensed under the MIT License.

#if !DISTRIBUTION_MSIX
using System.IO;
#endif

namespace Files.App.Utils.FileTags
{
	public sealed class FileTagsDatabase : IFileTagsDatabase
	{
#if DISTRIBUTION_MSIX
		private readonly FileTagRegistryStore registry = new();

		public void SetTags(string filePath, ulong? frn, string[] tags) => registry.SetTags(filePath, frn, tags);

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath) => registry.UpdateTag(oldFilePath, frn, newFilePath);

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath) => registry.UpdateTag(oldFrn, frn, newFilePath);

		public string[] GetTags(string? filePath, ulong? frn) => registry.GetTags(filePath, frn);

		public async Task<IEnumerable<TaggedFile>> GetAllAsync()
		{
			var items = await Server.FileTagsService.GetTaggedFilesAsync();
			return items.Select(item => new TaggedFile { FilePath = item.Path, Frn = item.FileId, Tags = item.Tags }).ToArray();
		}

		// Native file-operation callbacks need current registry entries without waiting on server RPC.
		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath) => registry.GetAllUnderPath(folderPath);

		public async Task<IEnumerable<TaggedFile>> GetAllUnderPathAsync(string folderPath)
		{
			var items = await GetAllAsync();
			if (string.IsNullOrEmpty(folderPath))
				return items;
			var folder = folderPath.Replace('/', '\\').TrimEnd('\\');
			var prefix = folder + "\\";
			return items.Where(item => item.FilePath.Equals(folder, StringComparison.OrdinalIgnoreCase)
				|| item.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		}

		public void Import(string json)
		{
			if (!registry.IsAvailable)
				return;

			var tags = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.TaggedFileArray);
			registry.ReplaceAll(tags);
		}

		public IEnumerable<TaggedFile> GetAll() => registry.GetAll();

		public string Export() => JsonSerializer.Serialize(registry.GetAll(), AppJsonSerializerContext.Default.ListTaggedFile);
#else
		private readonly IFileTagsDatabase _database = new PortableFileTagsDatabase(
			Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.FileTagSettingsDatabaseFileName));

		public void SetTags(string filePath, ulong? frn, string[] tags) => _database.SetTags(filePath, frn, tags);

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath) => _database.UpdateTag(oldFilePath, frn, newFilePath);

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath) => _database.UpdateTag(oldFrn, frn, newFilePath);

		public string[] GetTags(string? filePath, ulong? frn) => _database.GetTags(filePath, frn);

		public Task<IEnumerable<TaggedFile>> GetAllAsync() => Task.FromResult(_database.GetAll());

		public IEnumerable<TaggedFile> GetAll() => _database.GetAll();

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath) => _database.GetAllUnderPath(folderPath);

		public Task<IEnumerable<TaggedFile>> GetAllUnderPathAsync(string folderPath) => Task.FromResult(_database.GetAllUnderPath(folderPath));

		public void Import(string json) => _database.Import(json);

		public string Export() => _database.Export();
#endif
	}
}
