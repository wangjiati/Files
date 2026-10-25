// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Serialization
{
	internal interface ISettingsSerializer
	{
		string? FilePath { get; }

		bool CreateFile(string path);

		string ReadFromFile();

		bool WriteToFile(string text);
	}
}
