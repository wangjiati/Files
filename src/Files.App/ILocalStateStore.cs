// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App
{
	public interface ILocalStateStore
	{
		bool GetBoolean(string key, bool defaultValue = false);
		void SetBoolean(string key, bool value);
		int GetInt32(string key, int defaultValue = 0);
		void SetInt32(string key, int value);
		double GetDouble(string key, double defaultValue = 0d);
		void SetDouble(string key, double value);
		string? GetString(string key, string? defaultValue = null);
		void SetString(string key, string value);
		void Remove(string key);
	}
}
