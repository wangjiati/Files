// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
	/// <c>path " ?" ("filter=" filter ("&amp;" view)*)</c>. All values are
	/// percent-free quoted text so paths and filters survive copy/paste.
	/// </summary>
	public static class DirectoryAddressCodec
	{
		public const string ParameterPrefix = " ?";
		public const int MaxLength = 4096;

		private const int MaxClauses = 64;
		private const int MaxRegexLength = 512;

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

						if (!TryParseFilter(value, out filter, out error))
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

			var filterSuffix = FormatFilter(filter);
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

		private static bool TryParseFilter(string value, out DirectoryFilterSpec filter, out DirectoryAddressError error)
		{
			filter = DirectoryFilterSpec.Empty;
			error = DirectoryAddressError.None;

			if (!TrySplitOutsideQuotes(value, ';', out var clauses))
			{
				error = DirectoryAddressError.InvalidQuotes;
				return false;
			}

			if (clauses.Count > MaxClauses)
			{
				error = DirectoryAddressError.TooLong;
				return false;
			}

			string? nameContains = null;
			DirectoryRegexOptions? regex = null;
			var columns = new List<DirectoryColumnFilter>();
			var emptyColumns = new HashSet<DirectoryColumnKind>();
			long? sizeMin = null;
			long? sizeMax = null;
			DateTimeOffset? modifiedMin = null;
			DateTimeOffset? modifiedMax = null;
			DateTimeOffset? createdMin = null;
			DateTimeOffset? createdMax = null;

			foreach (var rawClause in clauses)
			{
				var clause = rawClause.Trim();
				if (clause.Length == 0)
				{
					error = DirectoryAddressError.MissingValue;
					return false;
				}

				if (clause.StartsWith("empty=", StringComparison.OrdinalIgnoreCase))
				{
					if (!TryParseEmpty(clause["empty=".Length..], emptyColumns, out error))
						return false;

					continue;
				}

				if (clause.StartsWith("regex", StringComparison.OrdinalIgnoreCase))
				{
					if (!TryParseRegex(clause, out regex, out error))
						return false;

					continue;
				}

				var (columnToken, operatorToken, valueToken) = SplitClause(clause);
				if (columnToken is null || operatorToken is null || valueToken is null)
				{
					error = DirectoryAddressError.MissingValue;
					return false;
				}

				switch (columnToken.ToLowerInvariant())
				{
					case "name":
						if (!TryParseTextOperator(operatorToken, out var textOperator))
						{
							error = DirectoryAddressError.InvalidOperator;
							return false;
						}

						if (!TryUnquote(valueToken, out var text))
						{
							error = DirectoryAddressError.InvalidQuotes;
							return false;
						}

						if (text.Length == 0)
						{
							error = DirectoryAddressError.MissingValue;
							return false;
						}

						if (textOperator is DirectoryTextOperator.Contains)
							nameContains = text;
						else
						{
							columns.RemoveAll(column => column.Column is DirectoryColumnKind.Name);
							columns.Add(new DirectoryColumnFilter
							{
								Column = DirectoryColumnKind.Name,
								Operator = textOperator,
								Text = text,
							});
						}
						break;

					case "type":
					case "ext":
					case "tag":
						if (!operatorToken.Equals("in", StringComparison.OrdinalIgnoreCase))
						{
							error = DirectoryAddressError.InvalidOperator;
							return false;
						}

						if (!TryParseValueList(valueToken, out var values))
						{
							error = DirectoryAddressError.InvalidQuotes;
							return false;
						}

						var columnKind = columnToken.ToLowerInvariant() switch
						{
							"type" => DirectoryColumnKind.Type,
							"ext" => DirectoryColumnKind.Extension,
							_ => DirectoryColumnKind.Tags,
						};
						columns.RemoveAll(column => column.Column == columnKind);
						columns.Add(new DirectoryColumnFilter
						{
							Column = columnKind,
							Values = values,
						});
						break;

					case "size":
						if (!TryApplyRange(operatorToken, valueToken, TryParseSize, ref sizeMin, ref sizeMax, out error))
							return false;
						break;

					case "modified":
						if (!TryApplyRange(operatorToken, valueToken, TryParseDate, ref modifiedMin, ref modifiedMax, out error))
							return false;
						break;

					case "created":
						if (!TryApplyRange(operatorToken, valueToken, TryParseDate, ref createdMin, ref createdMax, out error))
							return false;
						break;

					default:
						error = DirectoryAddressError.InvalidValue;
						return false;
				}
			}

			if (sizeMin is not null || sizeMax is not null)
				columns.Add(new DirectoryColumnFilter { Column = DirectoryColumnKind.Size, MinSize = sizeMin, MaxSize = sizeMax });

			if (modifiedMin is not null || modifiedMax is not null)
				columns.Add(new DirectoryColumnFilter { Column = DirectoryColumnKind.ModifiedDate, MinDate = modifiedMin, MaxDate = modifiedMax });

			if (createdMin is not null || createdMax is not null)
				columns.Add(new DirectoryColumnFilter { Column = DirectoryColumnKind.CreatedDate, MinDate = createdMin, MaxDate = createdMax });

			for (var index = 0; index < columns.Count; index++)
			{
				if (emptyColumns.Contains(columns[index].Column))
					columns[index] = columns[index] with { IncludeEmpty = true };
			}

			foreach (var emptyColumn in emptyColumns)
			{
				if (!columns.Any(column => column.Column == emptyColumn))
					columns.Add(new DirectoryColumnFilter { Column = emptyColumn, IncludeEmpty = true });
			}

			columns.Sort((left, right) => ColumnRank(left.Column).CompareTo(ColumnRank(right.Column)));
			filter = new DirectoryFilterSpec
			{
				NameContains = nameContains,
				Columns = columns,
				Regex = regex,
			};

			return true;
		}

		private static string FormatFilter(DirectoryFilterSpec filter)
		{
			var clauses = new List<string>();

			if (!string.IsNullOrEmpty(filter.NameContains))
				clauses.Add("name contains " + Quote(filter.NameContains));

			foreach (var column in filter.Columns.OrderBy(column => ColumnRank(column.Column)))
			{
				switch (column.Column)
				{
					case DirectoryColumnKind.Name:
						if (string.IsNullOrEmpty(column.Text))
							continue;

						clauses.Add("name " + TextOperatorToken(column.Operator) + " " + Quote(column.Text));
						break;

					case DirectoryColumnKind.Type:
					case DirectoryColumnKind.Extension:
					case DirectoryColumnKind.Tags:
						if (column.Values.Count > 0)
							clauses.Add(ColumnToken(column.Column) + " in " + string.Join(",", column.Values.Select(Quote)));

						if (column.IncludeEmpty)
							clauses.Add("empty=" + ColumnToken(column.Column));
						break;

					case DirectoryColumnKind.Size:
						AppendRange(clauses, "size", column.MinSize, column.MaxSize, FormatSize);
						break;

					case DirectoryColumnKind.ModifiedDate:
						AppendRange(clauses, "modified", column.MinDate, column.MaxDate, FormatDate);
						break;

					case DirectoryColumnKind.CreatedDate:
						AppendRange(clauses, "created", column.MinDate, column.MaxDate, FormatDate);
						break;
				}
			}

			if (filter.Regex is { } regex)
			{
				var target = RegexTargetToken(regex.Target);
				var flag = regex.IgnoreCase ? "i" : "c";
				clauses.Add("regex[" + target + "," + flag + "]=" + Quote("/" + regex.Pattern + "/"));
			}

			return string.Join(";", clauses);
		}

		private static void AppendRange<T>(List<string> clauses, string key, T? min, T? max, Func<T, string> format)
			where T : struct
		{
			if (min is { } minValue && max is { } maxValue &&
				EqualityComparer<T>.Default.Equals(minValue, maxValue))
			{
				clauses.Add(key + " = " + format(minValue));
				return;
			}

			if (min is { } minOnly)
				clauses.Add(key + " >= " + format(minOnly));

			if (max is { } maxOnly)
				clauses.Add(key + " <= " + format(maxOnly));
		}

		private static bool TryApplyRange<T>(
			string operatorToken,
			string rawValue,
			TryParseValue<T> parse,
			ref T? min,
			ref T? max,
			out DirectoryAddressError error)
			where T : struct
		{
			error = DirectoryAddressError.None;

			if (!parse(rawValue, out var value))
			{
				error = DirectoryAddressError.InvalidValue;
				return false;
			}

			switch (operatorToken)
			{
				case ">=":
					min = value;
					return true;

				case "<=":
					max = value;
					return true;

				case "=":
					min = value;
					max = value;
					return true;

				default:
					error = DirectoryAddressError.InvalidOperator;
					return false;
			}
		}

		private delegate bool TryParseValue<T>(string raw, out T value);

		private static bool TryParseEmpty(string value, HashSet<DirectoryColumnKind> columns, out DirectoryAddressError error)
		{
			error = DirectoryAddressError.None;

			var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				error = DirectoryAddressError.MissingValue;
				return false;
			}

			foreach (var part in parts)
			{
				switch (part.ToLowerInvariant())
				{
					case "type":
						columns.Add(DirectoryColumnKind.Type);
						break;

					case "ext":
						columns.Add(DirectoryColumnKind.Extension);
						break;

					case "tag":
						columns.Add(DirectoryColumnKind.Tags);
						break;

					default:
						error = DirectoryAddressError.UnsupportedEmptyColumn;
						return false;
				}
			}

			return true;
		}

		private static bool TryParseRegex(string clause, out DirectoryRegexOptions? regex, out DirectoryAddressError error)
		{
			regex = null;
			error = DirectoryAddressError.None;

			var rest = clause["regex".Length..].TrimStart();
			var target = DirectoryRegexTarget.RawName;
			var ignoreCase = true;

			if (rest.StartsWith('['))
			{
				var closeIndex = rest.IndexOf(']');
				if (closeIndex < 0)
				{
					error = DirectoryAddressError.InvalidValue;
					return false;
				}

				var flags = rest[1..closeIndex].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				foreach (var flag in flags)
				{
					switch (flag.ToLowerInvariant())
					{
						case "rawname":
							target = DirectoryRegexTarget.RawName;
							break;

						case "displayname":
							target = DirectoryRegexTarget.DisplayName;
							break;

						case "fullpath":
							target = DirectoryRegexTarget.FullPath;
							break;

						case "i":
							ignoreCase = true;
							break;

						case "c":
							ignoreCase = false;
							break;

						default:
							error = DirectoryAddressError.InvalidValue;
							return false;
					}
				}

				rest = rest[(closeIndex + 1)..];
			}

			rest = rest.TrimStart();
			if (!rest.StartsWith('='))
			{
				error = DirectoryAddressError.InvalidValue;
				return false;
			}

			var rawPattern = rest[1..].TrimStart();
			if (rawPattern.StartsWith('"'))
			{
				if (!TryUnquote(rawPattern, out var unquoted))
				{
					error = DirectoryAddressError.InvalidQuotes;
					return false;
				}

				rawPattern = unquoted;
			}

			if (rawPattern.Length < 2 || !rawPattern.StartsWith('/') || !rawPattern.EndsWith('/'))
			{
				error = DirectoryAddressError.InvalidValue;
				return false;
			}

			var pattern = rawPattern[1..^1];
			if (pattern.Length > MaxRegexLength)
			{
				error = DirectoryAddressError.InvalidValue;
				return false;
			}

			regex = new DirectoryRegexOptions(pattern, target, ignoreCase);
			return true;
		}

		private static (string? Column, string? Operator, string? Value) SplitClause(string clause)
		{
			var firstSpace = clause.IndexOf(' ');
			if (firstSpace <= 0)
				return (null, null, null);

			var column = clause[..firstSpace];
			var rest = clause[(firstSpace + 1)..].TrimStart();
			var secondSpace = rest.IndexOf(' ');
			if (secondSpace <= 0)
				return (null, null, null);

			var operatorToken = rest[..secondSpace];
			var value = rest[(secondSpace + 1)..].Trim();
			return value.Length == 0
				? (null, null, null)
				: (column, operatorToken, value);
		}

		private static bool TryParseValueList(string raw, out IReadOnlyList<string> values)
		{
			values = [];
			if (!TrySplitOutsideQuotes(raw, ',', out var parts))
				return false;

			var result = new List<string>(parts.Count);
			foreach (var part in parts)
			{
				if (!TryUnquote(part, out var value) || value.Length == 0)
					return false;

				result.Add(value);
			}

			values = result;
			return result.Count > 0;
		}

		private static bool TryUnquote(string raw, out string value)
		{
			value = raw.Trim();
			if (value.Length == 0 || value[0] != '"')
				return true;

			if (value.Length < 2 || value[^1] != '"')
				return false;

			var inner = value[1..^1];
			var builder = new StringBuilder(inner.Length);
			for (var index = 0; index < inner.Length; index++)
			{
				if (inner[index] != '"')
				{
					builder.Append(inner[index]);
					continue;
				}

				if (index + 1 < inner.Length && inner[index + 1] == '"')
				{
					builder.Append('"');
					index++;
					continue;
				}

				return false;
			}

			value = builder.ToString();
			return true;
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

		private static bool TryParseTextOperator(string token, out DirectoryTextOperator value)
		{
			switch (token.ToLowerInvariant())
			{
				case "contains":
					value = DirectoryTextOperator.Contains;
					return true;

				case "notcontains":
					value = DirectoryTextOperator.NotContains;
					return true;

				case "startswith":
					value = DirectoryTextOperator.StartsWith;
					return true;

				case "endswith":
					value = DirectoryTextOperator.EndsWith;
					return true;

				case "equals":
					value = DirectoryTextOperator.Equals;
					return true;

				default:
					value = DirectoryTextOperator.Contains;
					return false;
			}
		}

		private static string TextOperatorToken(DirectoryTextOperator value)
			=> value switch
			{
				DirectoryTextOperator.NotContains => "notcontains",
				DirectoryTextOperator.StartsWith => "startswith",
				DirectoryTextOperator.EndsWith => "endswith",
				DirectoryTextOperator.Equals => "equals",
				_ => "contains",
			};

		private static int ColumnRank(DirectoryColumnKind column)
			=> column switch
			{
				DirectoryColumnKind.Name => 0,
				DirectoryColumnKind.Type => 1,
				DirectoryColumnKind.Extension => 2,
				DirectoryColumnKind.Tags => 3,
				DirectoryColumnKind.Size => 4,
				DirectoryColumnKind.ModifiedDate => 5,
				DirectoryColumnKind.CreatedDate => 6,
				_ => 7,
			};

		private static string ColumnToken(DirectoryColumnKind column)
			=> column switch
			{
				DirectoryColumnKind.Type => "type",
				DirectoryColumnKind.Extension => "ext",
				DirectoryColumnKind.Tags => "tag",
				DirectoryColumnKind.Size => "size",
				DirectoryColumnKind.ModifiedDate => "modified",
				DirectoryColumnKind.CreatedDate => "created",
				_ => "name",
			};

		private static string Quote(string value)
			=> "\"" + value.Replace("\"", "\"\"") + "\"";

		private static bool TryParseSize(string raw, out long bytes)
		{
			bytes = 0;
			var value = raw.Trim().Trim('"');
			var unitStart = value.Length;
			while (unitStart > 0 && char.IsLetter(value[unitStart - 1]))
				unitStart--;

			var numberPart = value[..unitStart];
			var unit = value[unitStart..].ToUpperInvariant();
			if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
				return false;

			var multiplier = unit switch
			{
				"" or "B" => 1d,
				"KB" => 1024d,
				"MB" => 1024d * 1024,
				"GB" => 1024d * 1024 * 1024,
				"TB" => 1024d * 1024 * 1024 * 1024,
				_ => 0d,
			};

			if (multiplier == 0)
				return false;

			var result = number * multiplier;
			if (result > long.MaxValue)
				return false;

			bytes = (long)Math.Round(result, MidpointRounding.AwayFromZero);
			return true;
		}

		private static string FormatSize(long bytes)
		{
			var formatted = FormatSizeCore(bytes);
			return TryParseSize(formatted, out var roundTrip) && roundTrip == bytes
				? formatted
				: bytes.ToString(CultureInfo.InvariantCulture) + "B";
		}

		private static string FormatSizeCore(long bytes)
		{
			const long kilo = 1024;
			const long mega = kilo * 1024;
			const long giga = mega * 1024;
			const long tera = giga * 1024;

			if (bytes >= tera)
				return FormatSizeUnit(bytes, tera, "TB");

			if (bytes >= giga)
				return FormatSizeUnit(bytes, giga, "GB");

			if (bytes >= mega)
				return FormatSizeUnit(bytes, mega, "MB");

			if (bytes >= kilo)
				return FormatSizeUnit(bytes, kilo, "KB");

			return bytes.ToString(CultureInfo.InvariantCulture) + "B";
		}

		private static string FormatSizeUnit(long bytes, long unit, string suffix)
		{
			if (bytes % unit == 0)
				return (bytes / unit).ToString(CultureInfo.InvariantCulture) + suffix;

			return ((double)bytes / unit).ToString("0.#", CultureInfo.InvariantCulture) + suffix;
		}

		private static bool TryParseDate(string raw, out DateTimeOffset value)
			=> DateTimeOffset.TryParse(
				raw.Trim().Trim('"'),
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out value);

		private static string FormatDate(DateTimeOffset value)
			=> value.ToString("O", CultureInfo.InvariantCulture);

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

		private static string RegexTargetToken(DirectoryRegexTarget target)
			=> target switch
			{
				DirectoryRegexTarget.DisplayName => "displayname",
				DirectoryRegexTarget.FullPath => "fullpath",
				_ => "rawname",
			};
	}
}
