// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Provides the persisted date time format rules consumed by the resolver.
	/// </summary>
	public interface IDateTimeFormatRuleSource
	{
		/// <summary>
		/// Gets the global per-column rules.
		/// </summary>
		IReadOnlyList<DateTimeFormatRule> ColumnFormatRules { get; }

		/// <summary>
		/// Gets the directory-scoped rules.
		/// </summary>
		IReadOnlyList<DateTimeFormatRule> DirectoryFormatRules { get; }

		/// <summary>
		/// Gets the global default format.
		/// </summary>
		DateTimeFormats DefaultDateTimeFormat { get; }

		/// <summary>
		/// Gets the custom pattern used by the global default format.
		/// </summary>
		string? DefaultDateTimeFormatPattern { get; }
	}
}
