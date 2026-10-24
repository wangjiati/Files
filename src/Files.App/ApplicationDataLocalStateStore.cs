// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;

namespace Files.App
{
	internal sealed class ApplicationDataLocalStateStore : ILocalStateStore
	{
		private readonly ApplicationDataContainer _container;

		public ApplicationDataLocalStateStore(string? containerName = null)
		{
			_container = containerName is null
				? ApplicationData.Current.LocalSettings
				: ApplicationData.Current.LocalSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);
		}

		public bool GetBoolean(string key, bool defaultValue = false) =>
			_container.Values.TryGetValue(key, out var value) && value is bool result ? result : defaultValue;

		public void SetBoolean(string key, bool value) => _container.Values[key] = value;

		public int GetInt32(string key, int defaultValue = 0) =>
			_container.Values.TryGetValue(key, out var value) && value is int result ? result : defaultValue;

		public void SetInt32(string key, int value) => _container.Values[key] = value;

		public double GetDouble(string key, double defaultValue = 0d) =>
			_container.Values.TryGetValue(key, out var value) && value is double result ? result : defaultValue;

		public void SetDouble(string key, double value) => _container.Values[key] = value;

		public string? GetString(string key, string? defaultValue = null) =>
			_container.Values.TryGetValue(key, out var value) && value is string result ? result : defaultValue;

		public void SetString(string key, string value) => _container.Values[key] = value;

		public void Remove(string key) => _container.Values.Remove(key);
	}
}
