// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using System.Globalization;

namespace Files.App.Helpers
{
	public static class NewFolderNameFormatter
	{
		public static string Generate(
			string baseName,
			DateTimeOffset timestamp,
			IReadOnlyList<NewFolderNameComponent> components,
			string separator)
		{
			var values = components.Select(component => component switch
			{
				NewFolderNameComponent.Name => baseName,
				NewFolderNameComponent.Date => timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				NewFolderNameComponent.Time => timestamp.ToString("HH-mm-ss", CultureInfo.InvariantCulture),
				NewFolderNameComponent.YearMonth => timestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture),
				_ => throw new ArgumentOutOfRangeException(nameof(components)),
			});

			return string.Join(NormalizeSeparator(separator), values);
		}

		public static IReadOnlyList<NewFolderNameComponent> ParseComponents(IEnumerable<string>? components)
		{
			List<NewFolderNameComponent> result = [];
			if (components is not null)
			{
				foreach (var value in components)
				{
					var component = value switch
					{
						nameof(NewFolderNameComponent.Name) => NewFolderNameComponent.Name,
						nameof(NewFolderNameComponent.Date) => NewFolderNameComponent.Date,
						nameof(NewFolderNameComponent.Time) => NewFolderNameComponent.Time,
						nameof(NewFolderNameComponent.YearMonth) => NewFolderNameComponent.YearMonth,
						_ => (NewFolderNameComponent?)null,
					};

					if (component is { } parsed && !result.Contains(parsed))
						result.Add(parsed);
				}
			}

			if (result.Count == 0)
				result.Add(NewFolderNameComponent.Name);

			return result;
		}

		public static string NormalizeSeparator(string? separator)
			=> separator is "_" or "-" or " " or "" ? separator : "_";
	}
}
