// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.FileTags
{
	public interface IFileTagsDatabase
	{
		void SetTags(string filePath, ulong? frn, string[] tags);
		void UpdateTag(string oldFilePath, ulong? frn, string? newFilePath);
		void UpdateTag(ulong oldFrn, ulong? frn, string? newFilePath);
		string[] GetTags(string? filePath, ulong? frn);
		IEnumerable<TaggedFile> GetAll();
		IEnumerable<TaggedFile> GetAllUnderPath(string folderPath);
		void Import(string json);
		string Export();
	}
}
