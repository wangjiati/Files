// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.DateTimeFormatter
{
	public interface IDateTimeFormatResolver
	{
		/// <summary>
		/// Resolves the effective format rule for a column in a directory.
		/// </summary>
		/// <param name="directoryPath">The directory whose rules should be applied.</param>
		/// <param name="column">The date column to resolve.</param>
		/// <returns>The highest priority rule, or the global default when no rule matches.</returns>
		DateTimeFormatRule Resolve(string directoryPath, DateTimeColumnKind column);
	}
}
