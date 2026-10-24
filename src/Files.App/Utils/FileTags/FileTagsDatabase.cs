// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.FileTags
{
	public sealed class FileTagsDatabase : IFileTagsDatabase
	{
		private readonly IFileTagsDatabase _database;

		public FileTagsDatabase()
		{
			_database = AppRuntime.Current.Distribution is DistributionKind.Portable
				? new PortableFileTagsDatabase(Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.FileTagSettingsDatabaseFileName))
				: new RegistryFileTagsDatabase();
		}

		public void SetTags(string filePath, ulong? frn, string[] tags) => _database.SetTags(filePath, frn, tags);

		public void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath) => _database.UpdateTag(oldFilePath, frn, newFilePath);

		public void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath) => _database.UpdateTag(oldFrn, frn, newFilePath);

		public string[] GetTags(string? filePath, ulong? frn) => _database.GetTags(filePath, frn);

		public IEnumerable<TaggedFile> GetAll() => _database.GetAll();

		public IEnumerable<TaggedFile> GetAllUnderPath(string folderPath) => _database.GetAllUnderPath(folderPath);

		public void Import(string json) => _database.Import(json);

		public string Export() => _database.Export();
	}
}
