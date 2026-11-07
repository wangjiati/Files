// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Files.App.Services.Update;

public static class UpdateVersionHelper
{
	private static readonly Regex VersionPattern = new(@"v?(\d+(?:\.\d+){2,3})", RegexOptions.CultureInvariant);

	public static bool TryParseVersion(string? text, out Version version)
	{
		version = new Version(0, 0, 0, 0);

		if (string.IsNullOrWhiteSpace(text))
			return false;

		var match = VersionPattern.Match(text);
		if (!match.Success)
			return false;

		var parts = match.Groups[1].Value.Split('.');
		var values = new int[4];

		for (var i = 0; i < values.Length && i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
				return false;
		}

		version = new Version(values[0], values[1], values[2], values[3]);
		return true;
	}

	public static bool IsNewer(Version candidate, Version current)
		=> candidate > current;
}
