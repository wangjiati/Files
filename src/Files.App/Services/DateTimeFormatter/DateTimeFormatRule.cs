// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Represents an immutable date time format rule for a column and optional directory scope.
	/// </summary>
	/// <param name="Column">The column the rule applies to, or <see langword="null"/> for the scope default.</param>
	/// <param name="DirectoryPath">The directory the rule applies to, or <see langword="null"/> for a global rule.</param>
	/// <param name="IncludeSubdirectories">Whether the rule also applies to subdirectories.</param>
	/// <param name="Format">The preset format, or <see cref="DateTimeFormats.Custom"/>.</param>
	/// <param name="CustomPattern">The custom .NET format pattern when <paramref name="Format"/> is custom.</param>
	public sealed record DateTimeFormatRule(
		DateTimeColumnKind? Column,
		string? DirectoryPath,
		bool IncludeSubdirectories,
		DateTimeFormats Format,
		string? CustomPattern)
	{
		/// <summary>
		/// Validates the rule without throwing, so invalid persisted settings cannot break list rendering.
		/// </summary>
		/// <returns>An error message when the rule is invalid; otherwise, <see langword="null"/>.</returns>
		public string? Validate()
		{
			if (!Enum.IsDefined(Format))
				return $"Unsupported date time format: {Format}.";

			if (Format is not DateTimeFormats.Custom)
				return null;

			return CustomDateTimeFormatter.TryValidatePattern(CustomPattern, out var error) ? null : error;
		}
	}
}
