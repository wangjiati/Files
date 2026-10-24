// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	public interface ILayoutPreferencesDatabase
	{
		LayoutPreferencesItem? GetPreferences(string filePath, ulong? frn = null);
		void SetPreferences(string filePath, ulong? frn, LayoutPreferencesItem? preferencesItem);
		void ResetAll();
		void Import(string json);
		string Export();
	}
}
