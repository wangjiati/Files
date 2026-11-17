// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Files.App.Utils.Storage
{
	public enum DirectoryAddressError
	{
		None,
		TooLong,
		MissingValue,
		InvalidQuotes,
		UnsupportedEmptyColumn,
		InvalidOperator,
		InvalidValue,
	}

	/// <summary>
	/// A path plus the transient filter and view state carried by a view address.
	/// </summary>
	public sealed record DirectoryAddress(string Path, DirectoryFilterSpec Filter, DirectoryViewOverrides View)
	{
		public bool HasParameters => !Filter.IsEmpty || !View.IsEmpty;
	}

	/// <summary>
	/// Encodes and decodes the view address shown in the omnibar:
	/// <c>path " ?" ("filter=" filter ("&amp;" view)*)</c>. The filter is a LINQ-style
	/// expression, for example <c>Name.Contains("report") &amp;&amp; Size &gt;= 1MB</c>.
	/// </summary>
	public static class DirectoryAddressCodec
	{
		public const string ParameterPrefix = " ?";
		public const int MaxLength = 4096;

		public static bool TryParse(string? text, out DirectoryAddress address, out DirectoryAddressError error)
		{
			text ??= string.Empty;
			error = DirectoryAddressError.None;
			address = new DirectoryAddress(text, DirectoryFilterSpec.Empty, DirectoryViewOverrides.Empty);

			// FTP paths may legally contain a space followed by '?'; never split them.
			if (IsFtpPath(text))
				return true;

			var separatorIndex = text.IndexOf(ParameterPrefix, StringComparison.Ordinal);
			if (separatorIndex < 0)
				return true;

			var parameters = text[(separatorIndex + ParameterPrefix.Length)..];
			if (!HasKnownParameter(parameters))
				return true;

			if (text.Length > MaxLength)
			{
				error = DirectoryAddressError.TooLong;
				return false;
			}

			if (!TrySplitOutsideQuotes(parameters, '&', out var segments))
			{
				error = DirectoryAddressError.InvalidQuotes;
				return false;
			}

			var path = text[..separatorIndex];
			var filter = DirectoryFilterSpec.Empty;
			var view = DirectoryViewOverrides.Empty;

			foreach (var segment in segments)
			{
				var equalsIndex = segment.IndexOf('=');
				if (equalsIndex <= 0)
					continue;

				var key = segment[..equalsIndex].Trim();
				var value = segment[(equalsIndex + 1)..];

				switch (key.ToLowerInvariant())
				{
					case "filter":
						if (value.Trim().Length == 0)
							break;

						if (!DirectoryAddressFilterCodec.TryParse(value, out filter, out error))
							return false;
						break;

					case "group":
						if (TryParseGroup(value, out var groupOption, out var groupDirection, out var groupUnit))
							view = view with
							{
								GroupOption = groupOption,
								GroupDirection = groupDirection,
								GroupByDateUnit = groupUnit,
							};
						break;

					case "sort":
						if (TryParseSort(value, out var sortOption, out var sortDirection))
							view = view with
							{
								SortOption = sortOption,
								SortDirection = sortDirection,
							};
						break;

					case "layout":
						if (TryParseLayout(value, out var layoutMode))
							view = view with { LayoutMode = layoutMode };
						break;
				}
			}

			address = new DirectoryAddress(path, filter, view);
			return true;
		}

		public static string Format(string path, DirectoryFilterSpec filter, DirectoryViewOverrides view)
		{
			var suffix = FormatSuffix(filter, view);
			return suffix.Length == 0 ? path : path + " " + suffix;
		}

		public static string FormatSuffix(DirectoryFilterSpec filter, DirectoryViewOverrides view)
		{
			var segments = new List<string>(4);

			var filterSuffix = DirectoryAddressFilterCodec.Format(filter);
			if (filterSuffix.Length > 0)
				segments.Add("filter=" + filterSuffix);

			if (view.GroupOption is { } groupOption)
			{
				var group = GroupOptionToken(groupOption);
				if (view.GroupDirection is { } groupDirection)
				{
					group += "," + DirectionToken(groupDirection);
					if (view.GroupByDateUnit is { } groupByDateUnit)
						group += "," + UnitToken(groupByDateUnit);
				}

				segments.Add("group=" + group);
			}

			if (view.SortOption is { } sortOption)
			{
				var sort = SortOptionToken(sortOption);
				if (view.SortDirection is { } sortDirection)
					sort += "," + DirectionToken(sortDirection);

				segments.Add("sort=" + sort);
			}

			if (view.LayoutMode is { } layoutMode && TryGetLayoutToken(layoutMode, out var layoutToken))
				segments.Add("layout=" + layoutToken);

			return segments.Count == 0 ? string.Empty : "?" + string.Join("&", segments);
		}

		public static string GetPathPart(string? text)
		{
			text ??= string.Empty;
			if (IsFtpPath(text))
				return text;

			var separatorIndex = text.IndexOf(ParameterPrefix, StringComparison.Ordinal);
			if (separatorIndex < 0)
				return text;

			return HasKnownParameter(text[(separatorIndex + ParameterPrefix.Length)..])
				? text[..separatorIndex]
				: text;
		}

		private static bool TrySplitOutsideQuotes(string text, char separator, out List<string> parts)
		{
			parts = [];
			var builder = new StringBuilder();
			var inQuotes = false;

			for (var index = 0; index < text.Length; index++)
			{
				var character = text[index];
				if (character == '"')
				{
					if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
					{
						builder.Append("\"\"");
						index++;
						continue;
					}

					inQuotes = !inQuotes;
					builder.Append(character);
					continue;
				}

				if (character == separator && !inQuotes)
				{
					// A LINQ filter joins terms with '&&'; only a single '&' separates address parameters.
					if (separator == '&' && index + 1 < text.Length && text[index + 1] == '&')
					{
						builder.Append("&&");
						index++;
						continue;
					}

					parts.Add(builder.ToString());
					builder.Clear();
					continue;
				}

				builder.Append(character);
			}

			if (inQuotes)
				return false;

			parts.Add(builder.ToString());
			return true;
		}

		private static bool IsFtpPath(string text)
			=> text.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
			   text.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
			   text.StartsWith("ftpes://", StringComparison.OrdinalIgnoreCase);

		private static bool HasKnownParameter(string parameters)
		{
			foreach (var segment in parameters.Split('&'))
			{
				var equalsIndex = segment.IndexOf('=');
				if (equalsIndex <= 0)
					continue;

				var key = segment[..equalsIndex].Trim();
				if (key.Equals("filter", StringComparison.OrdinalIgnoreCase) ||
					key.Equals("group", StringComparison.OrdinalIgnoreCase) ||
					key.Equals("sort", StringComparison.OrdinalIgnoreCase) ||
					key.Equals("layout", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static bool TryParseGroup(
			string value,
			out GroupOption option,
			out SortDirection? direction,
			out GroupByDateUnit? unit)
		{
			option = GroupOption.None;
			direction = null;
			unit = null;

			var parts = value.Split(',', StringSplitOptions.TrimEntries);
			if (parts.Length is 0 or > 3 || !TryParseGroupOption(parts[0], out option))
				return false;

			if (parts.Length > 1)
			{
				if (!TryParseDirection(parts[1], out var parsedDirection))
					return false;

				direction = parsedDirection;
			}

			if (parts.Length > 2)
			{
				if (!TryParseUnit(parts[2], out var parsedUnit))
					return false;

				unit = parsedUnit;
			}

			return true;
		}

		private static bool TryParseSort(string value, out SortOption option, out SortDirection? direction)
		{
			option = SortOption.Name;
			direction = null;

			var parts = value.Split(',', StringSplitOptions.TrimEntries);
			if (parts.Length is 0 or > 2 || !TryParseSortOption(parts[0], out option))
				return false;

			if (parts.Length > 1)
			{
				if (!TryParseDirection(parts[1], out var parsedDirection))
					return false;

				direction = parsedDirection;
			}

			return true;
		}

		private static bool TryParseLayout(string value, out FolderLayoutModes layout)
		{
			layout = FolderLayoutModes.DetailsView;
			switch (value.Trim().ToLowerInvariant())
			{
				case "details":
					layout = FolderLayoutModes.DetailsView;
					return true;

				case "list":
					layout = FolderLayoutModes.ListView;
					return true;

				case "cards":
					layout = FolderLayoutModes.CardsView;
					return true;

				case "columns":
					layout = FolderLayoutModes.ColumnView;
					return true;

				case "grid":
					layout = FolderLayoutModes.GridView;
					return true;

				default:
					return false;
			}
		}

		private static bool TryParseDirection(string value, out SortDirection direction)
		{
			switch (value.Trim().ToLowerInvariant())
			{
				case "asc":
					direction = SortDirection.Ascending;
					return true;

				case "desc":
					direction = SortDirection.Descending;
					return true;

				default:
					direction = SortDirection.Ascending;
					return false;
			}
		}

		private static bool TryParseUnit(string value, out GroupByDateUnit unit)
		{
			switch (value.Trim().ToLowerInvariant())
			{
				case "day":
					unit = GroupByDateUnit.Day;
					return true;

				case "month":
					unit = GroupByDateUnit.Month;
					return true;

				case "year":
					unit = GroupByDateUnit.Year;
					return true;

				default:
					unit = GroupByDateUnit.Year;
					return false;
			}
		}

		private static bool TryParseGroupOption(string value, out GroupOption option)
		{
			switch (value.Trim().ToLowerInvariant())
			{
				case "none":
					option = GroupOption.None;
					return true;

				case "name":
					option = GroupOption.Name;
					return true;

				case "modified":
					option = GroupOption.DateModified;
					return true;

				case "created":
					option = GroupOption.DateCreated;
					return true;

				case "size":
					option = GroupOption.Size;
					return true;

				case "type":
					option = GroupOption.FileType;
					return true;

				case "sync":
					option = GroupOption.SyncStatus;
					return true;

				case "tag":
					option = GroupOption.FileTag;
					return true;

				case "folder":
					option = GroupOption.OriginalFolder;
					return true;

				case "deleted":
					option = GroupOption.DateDeleted;
					return true;

				case "path":
					option = GroupOption.FolderPath;
					return true;

				default:
					option = GroupOption.None;
					return false;
			}
		}

		private static bool TryParseSortOption(string value, out SortOption option)
		{
			switch (value.Trim().ToLowerInvariant())
			{
				case "name":
					option = SortOption.Name;
					return true;

				case "modified":
					option = SortOption.DateModified;
					return true;

				case "created":
					option = SortOption.DateCreated;
					return true;

				case "size":
					option = SortOption.Size;
					return true;

				case "type":
					option = SortOption.FileType;
					return true;

				case "sync":
					option = SortOption.SyncStatus;
					return true;

				case "tag":
					option = SortOption.FileTag;
					return true;

				case "folder":
					option = SortOption.OriginalFolder;
					return true;

				case "deleted":
					option = SortOption.DateDeleted;
					return true;

				case "path":
					option = SortOption.Path;
					return true;

				default:
					option = SortOption.Name;
					return false;
			}
		}

		private static string GroupOptionToken(GroupOption option)
			=> option switch
			{
				GroupOption.Name => "name",
				GroupOption.DateModified => "modified",
				GroupOption.DateCreated => "created",
				GroupOption.Size => "size",
				GroupOption.FileType => "type",
				GroupOption.SyncStatus => "sync",
				GroupOption.FileTag => "tag",
				GroupOption.OriginalFolder => "folder",
				GroupOption.DateDeleted => "deleted",
				GroupOption.FolderPath => "path",
				_ => "none",
			};

		private static string SortOptionToken(SortOption option)
			=> option switch
			{
				SortOption.DateModified => "modified",
				SortOption.DateCreated => "created",
				SortOption.Size => "size",
				SortOption.FileType => "type",
				SortOption.SyncStatus => "sync",
				SortOption.FileTag => "tag",
				SortOption.OriginalFolder => "folder",
				SortOption.DateDeleted => "deleted",
				SortOption.Path => "path",
				_ => "name",
			};

		private static string DirectionToken(SortDirection direction)
			=> direction is SortDirection.Descending ? "desc" : "asc";

		private static string UnitToken(GroupByDateUnit unit)
			=> unit switch
			{
				GroupByDateUnit.Day => "day",
				GroupByDateUnit.Month => "month",
				_ => "year",
			};

		private static bool TryGetLayoutToken(FolderLayoutModes layout, out string token)
		{
			switch (layout)
			{
				case FolderLayoutModes.DetailsView:
					token = "details";
					return true;

				case FolderLayoutModes.ListView:
					token = "list";
					return true;

				case FolderLayoutModes.CardsView:
					token = "cards";
					return true;

				case FolderLayoutModes.ColumnView:
					token = "columns";
					return true;

				case FolderLayoutModes.GridView:
					token = "grid";
					return true;

				default:
					token = string.Empty;
					return false;
			}
		}
	}
}
