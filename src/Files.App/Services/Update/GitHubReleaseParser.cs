// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;

namespace Files.App.Services.Update;

public sealed record GitHubReleaseInfo(Version Version, string? Notes, Uri PageUrl);

public static class GitHubReleaseParser
{
	public static bool TryParseLatestRelease(string json, out GitHubReleaseInfo release)
	{
		release = null!;

		try
		{
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;

			if (!TryGetString(root, "tag_name", out var tag) ||
				!TryGetString(root, "html_url", out var url) ||
				!UpdateVersionHelper.TryParseVersion(tag, out var version) ||
				!Uri.TryCreate(url, UriKind.Absolute, out var pageUrl) ||
				pageUrl.Scheme != Uri.UriSchemeHttps ||
				!string.Equals(pageUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			_ = TryGetString(root, "body", out var notes);
			release = new GitHubReleaseInfo(version, notes, pageUrl);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool TryGetString(JsonElement element, string propertyName, out string? value)
	{
		if (element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String)
		{
			value = property.GetString();
			return true;
		}

		value = null;
		return false;
	}
}
