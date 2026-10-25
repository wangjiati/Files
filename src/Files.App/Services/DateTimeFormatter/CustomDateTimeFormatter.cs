// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Represents a formatter that uses a user-defined .NET date time format pattern.
	/// </summary>
	internal sealed class CustomDateTimeFormatter : AbstractDateTimeFormatter
	{
		internal const string DefaultPattern = "yyyy-MM-dd HH:mm:ss";

		private readonly string _pattern;

		public CustomDateTimeFormatter(string? pattern)
		{
			_pattern = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern;
		}

		public override string Name
			=> Strings.Custom.GetLocalizedResource();

		public override string ToShortLabel(DateTimeOffset offset)
		{
			// Check if the year is out of the valid range
			if (offset.Year is <= 1601 or >= 9999)
				return " ";

			try
			{
				return ToString(offset, _pattern);
			}
			catch (FormatException)
			{
				// Invalid settings must not break list rendering.
				return " ";
			}
		}

		internal static bool TryValidatePattern(string? pattern, out string? error)
		{
			if (string.IsNullOrWhiteSpace(pattern))
			{
				error = "A custom date time format is required.";
				return false;
			}

			try
			{
				_ = ToString(DateTimeOffset.Now, pattern);
			}
			catch (FormatException)
			{
				error = "The custom date time format is invalid.";
				return false;
			}

			error = null;
			return true;
		}
	}
}
