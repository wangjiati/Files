// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;

namespace Files.App.Services.Update;

public static class UpdateCheckPolicy
{
	public const string DateFormat = "yyyy-MM-dd";

	public static bool ShouldCheckToday(string? lastCheckDate, DateOnly today)
	{
		if (!DateOnly.TryParseExact(lastCheckDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			return true;

		return parsed != today;
	}

	public static bool IsSkipped(Version available, string? skippedVersion)
	{
		return !string.IsNullOrWhiteSpace(skippedVersion)
			&& UpdateVersionHelper.TryParseVersion(skippedVersion, out var skipped)
			&& skipped == available;
	}
}
