// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Represents a formatter that uses a user-defined .NET date time format pattern.
	/// </summary>
	internal sealed class CustomDateTimeFormatter : AbstractDateTimeFormatter
	{
		private readonly string? _pattern;
		private readonly CultureInfo _culture;
		private readonly bool _isPatternValid;

		public CustomDateTimeFormatter(string? pattern)
			: this(pattern, null)
		{
		}

		internal CustomDateTimeFormatter(string? pattern, CultureInfo? culture)
		{
			_pattern = pattern;
			_culture = culture ?? AppCulture;
			_isPatternValid = ValidatePattern(pattern, _culture) is DateTimeFormatRuleValidationError.None;
		}

		public override string Name
			=> Strings.Custom.GetLocalizedResource();

		public override string ToShortLabel(DateTimeOffset offset)
		{
			if (!_isPatternValid)
				return " ";

			// Check if the year is out of the valid range
			if (offset.Year is <= 1601 or >= 9999)
				return " ";

			try
			{
				return ToString(offset, _pattern!, _culture);
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException)
			{
				// Invalid settings and calendar range errors must not break list rendering.
				return " ";
			}
		}

		internal static DateTimeFormatRuleValidationError ValidatePattern(string? pattern, CultureInfo? culture = null)
		{
			if (string.IsNullOrWhiteSpace(pattern))
				return DateTimeFormatRuleValidationError.MissingPattern;

			try
			{
				_ = DateTimeOffset.Now.ToString(pattern, culture ?? AppCulture);
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException)
			{
				return DateTimeFormatRuleValidationError.InvalidPattern;
			}

			return DateTimeFormatRuleValidationError.None;
		}
	}
}
