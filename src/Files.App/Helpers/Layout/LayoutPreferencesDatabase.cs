// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Helpers
{
	public sealed class LayoutPreferencesDatabase : ILayoutPreferencesDatabase
	{
		private readonly ILayoutPreferencesDatabase _database;

		public LayoutPreferencesDatabase()
		{
			_database = AppRuntime.Current.Distribution is DistributionKind.Portable
				? new PortableLayoutPreferencesDatabase(Path.Combine(AppRuntime.Current.SettingsDirectory, Constants.LocalSettings.UserSettingsDatabaseFileName))
				: new RegistryLayoutPreferencesDatabase();
		}

		public LayoutPreferencesItem? GetPreferences(string filePath, ulong? frn) => _database.GetPreferences(filePath, frn);

		public void SetPreferences(string filePath, ulong? frn, LayoutPreferencesItem? preferencesItem) => _database.SetPreferences(filePath, frn, preferencesItem);

		public void ResetAll() => _database.ResetAll();

		public void Import(string json) => _database.Import(json);

		public string Export() => _database.Export();
	}
}
