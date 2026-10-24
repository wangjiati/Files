// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Text.Json.Nodes;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using static Files.App.Helpers.Win32Helper;
using static Files.App.Helpers.Win32PInvoke;

namespace Files.App.Utils.Serialization.Implementation
{
	internal sealed class DefaultSettingsSerializer : ISettingsSerializer
	{
		private string? _filePath;

		public bool CreateFile(string path)
		{
			PInvoke.CreateDirectoryFromApp(Path.GetDirectoryName(path), null);

			var hFile = CreateFileFromApp(path, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_ALWAYS, (uint)File_Attributes.BackupSemantics, IntPtr.Zero);
			if (hFile.IsHandleInvalid())
			{
				return false;
			}

			Win32PInvoke.CloseHandle(hFile);

			_filePath = path;
			return true;
		}

		/// <summary>
		/// Reads a file to a string
		/// </summary>
		/// <returns>A string value or string.Empty if nothing is present in the file</returns>
		/// <exception cref="ArgumentNullException"></exception>
		public string ReadFromFile()
		{
			ArgumentNullException.ThrowIfNull(_filePath);

			return VersionedJsonFile.Read(_filePath, payloadName: null, allowUnversioned: true)?.ToJsonString() ?? string.Empty;
		}

		public bool WriteToFile(string text)
		{
			ArgumentNullException.ThrowIfNull(_filePath);

			try
			{
				VersionedJsonFile.Write(_filePath, payloadName: null, JsonNode.Parse(text));
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}
	}
}
