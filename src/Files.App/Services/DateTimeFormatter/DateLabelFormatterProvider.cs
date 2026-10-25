// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Represents the formatters applied to an item's date labels for one shell context.
	/// </summary>
	internal sealed record DateLabelFormatters(
		IDateTimeFormatter Modified,
		IDateTimeFormatter Created,
		IDateTimeFormatter Accessed,
		IDateTimeFormatter Deleted)
	{
		/// <summary>
		/// Gets whether any column uses the relative application date format and must refresh every second.
		/// </summary>
		public bool UsesApplicationFormat { get; init; }
	}

	/// <summary>
	/// Resolves date column formatters for a working directory and applies them to listed items.
	/// </summary>
	internal sealed class DateLabelFormatterProvider
	{
		private readonly IDateTimeFormatResolver _resolver;
		private readonly IDateTimeFormatterFactory _factory;
		private readonly Func<DateTimeFormats> _defaultFormat;
		private readonly Func<string?> _defaultPattern;

		public DateLabelFormatterProvider(
			IDateTimeFormatResolver resolver,
			IDateTimeFormatterFactory factory,
			Func<DateTimeFormats> defaultFormat,
			Func<string?> defaultPattern)
		{
			_resolver = resolver;
			_factory = factory;
			_defaultFormat = defaultFormat;
			_defaultPattern = defaultPattern;
		}

		public DateLabelFormatters Resolve(string? workingDirectory)
		{
			var directoryPath = string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathRooted(workingDirectory)
				? string.Empty
				: workingDirectory;

			var modified = _resolver.Resolve(directoryPath, DateTimeColumnKind.Modified);
			var created = _resolver.Resolve(directoryPath, DateTimeColumnKind.Created);
			var deleted = _resolver.Resolve(directoryPath, DateTimeColumnKind.Deleted);
			var defaultFormat = _defaultFormat();
			var defaultPattern = _defaultPattern();

			return new DateLabelFormatters(
				Create(modified),
				Create(created),
				Create(defaultFormat, defaultPattern),
				Create(deleted))
			{
				UsesApplicationFormat = IsApplicationFormat(modified.Format)
					|| IsApplicationFormat(created.Format)
					|| IsApplicationFormat(defaultFormat)
					|| IsApplicationFormat(deleted.Format),
			};
		}

		public void Apply(IEnumerable<ListedItem> items, DateLabelFormatters formatters)
		{
			foreach (var item in items)
				Apply(item, formatters);
		}

		public void Apply(ListedItem item, DateLabelFormatters formatters)
		{
			if (item is RecycleBinItem recycleBinItem)
				recycleBinItem.RefreshDateLabels(formatters.Modified, formatters.Created, formatters.Accessed, formatters.Deleted);
			else
				item.RefreshDateLabels(formatters.Modified, formatters.Created, formatters.Accessed);
		}

		private IDateTimeFormatter Create(DateTimeFormatRule rule)
			=> Create(rule.Format, rule.CustomPattern);

		private IDateTimeFormatter Create(DateTimeFormats format, string? customPattern)
		{
			var validFormat = Enum.IsDefined(format) ? format : DateTimeFormats.Application;

			return _factory.GetDateTimeFormatter(validFormat, customPattern);
		}

		private static bool IsApplicationFormat(DateTimeFormats format)
			=> !Enum.IsDefined(format) || format is DateTimeFormats.Application;
	}
}
