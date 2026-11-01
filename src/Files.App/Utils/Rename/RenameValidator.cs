// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Describes why a file name was rejected.
	/// </summary>
	public enum RenameValidationError
	{
		Empty,
		IllegalCharacter,
		ReservedName,
		TrailingDotOrSpace,
		TooLong,
	}

	/// <summary>
	/// Validates a bare file name (no path, extension included) before a batch rename is applied.
	/// </summary>
	/// <remarks>
	/// Mirrors the naming rules of <see cref="Files.App.Utils.Storage.FilesystemHelpers"/> without touching
	/// user settings or the IoC container: that type's static initializer requires a configured service
	/// provider, so its members cannot be reused from setting-independent validation code.
	/// </remarks>
	public static class RenameValidator
	{
		// Default FilesystemHelpers.RestrictedCharacters; ":" is always disallowed here.
		private static readonly char[] RestrictedCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

		// FilesystemHelpers.RestrictedFileNames.
		private static readonly string[] ReservedFileNames =
		[
			"CON", "PRN", "AUX",
			"NUL", "COM1", "COM2",
			"COM3", "COM4", "COM5",
			"COM6", "COM7", "COM8",
			"COM9", "LPT1", "LPT2",
			"LPT3", "LPT4", "LPT5",
			"LPT6", "LPT7", "LPT8",
			"LPT9",
		];

		private const int MaxFileNameLength = 255;

		/// <summary>
		/// Returns the first reason the name is invalid, or null when the name is acceptable.
		/// </summary>
		public static RenameValidationError? Validate(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
				return RenameValidationError.Empty;

			if (fileName.IndexOfAny(RestrictedCharacters) >= 0)
				return RenameValidationError.IllegalCharacter;

			if (ContainsReservedFileName(fileName))
				return RenameValidationError.ReservedName;

			if (fileName[^1] is '.' or ' ')
				return RenameValidationError.TrailingDotOrSpace;

			if (fileName.Length > MaxFileNameLength)
				return RenameValidationError.TooLong;

			return null;
		}

		// Same rule as FilesystemHelpers.ContainsRestrictedFileName.
		private static bool ContainsReservedFileName(string input)
		{
			foreach (string name in ReservedFileNames)
			{
				if (input.StartsWith(name, StringComparison.OrdinalIgnoreCase) && (input.Length == name.Length || input[name.Length] == '.'))
					return true;
			}

			return false;
		}
	}
}
