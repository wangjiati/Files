// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Items;

namespace Files.App.Services.DateTimeFormatter
{
	public enum DateTimeFormatRuleValidationError
	{
		None = 0,
		InvalidColumn = 1,
		InvalidFormat = 2,
		MissingPattern = 3,
		InvalidPattern = 4,
		MissingRelativePath = 5,
		MissingDirectory = 6,
		UnsupportedDirectory = 7,
	}

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
		/// Gets the identity of the removable volume that contains <see cref="DirectoryPath"/>.
		/// </summary>
		public string? VolumeId { get; init; }

		/// <summary>
		/// Gets the path relative to the removable volume root.
		/// </summary>
		public string? RelativePath { get; init; }

		/// <summary>
		/// Validates the rule without throwing, so invalid persisted settings cannot break list rendering.
		/// </summary>
		/// <returns>A localizable validation error; <see cref="DateTimeFormatRuleValidationError.None"/> when valid.</returns>
		public DateTimeFormatRuleValidationError Validate()
		{
			if (Column is { } column && !Enum.IsDefined(column))
				return DateTimeFormatRuleValidationError.InvalidColumn;

			if (!string.IsNullOrWhiteSpace(VolumeId) && string.IsNullOrWhiteSpace(RelativePath))
				return DateTimeFormatRuleValidationError.MissingRelativePath;

			if (!Enum.IsDefined(Format))
				return DateTimeFormatRuleValidationError.InvalidFormat;

			if (Format is not DateTimeFormats.Custom)
				return DateTimeFormatRuleValidationError.None;

			return CustomDateTimeFormatter.ValidatePattern(CustomPattern);
		}
	}
}
