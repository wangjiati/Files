// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Update;

public static class UpdateNotesFormatter
{
	public const int DefaultMaxLength = 1500;

	public static string ToPreview(string? notes, int maxLength = DefaultMaxLength)
	{
		if (string.IsNullOrWhiteSpace(notes))
			return string.Empty;

		if (notes.Length <= maxLength)
			return notes;

		var trimmed = notes.AsSpan(0, maxLength).TrimEnd();
		return trimmed.ToString() + "…";
	}
}
