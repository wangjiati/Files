// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Encodes the filter carried by a view address as a LINQ-style boolean expression, for example
	/// <c>Name.Contains("report") &amp;&amp; Size &gt;= 1MB</c>, and parses it back into a filter spec.
	/// </summary>
	internal static class DirectoryAddressFilterCodec
	{
		private const int MaxTerms = 64;
		private const int MaxRegexLength = 512;

		public static string Format(DirectoryFilterSpec filter)
		{
			var terms = new List<(DirectoryConditionJoin Join, string Text)>();
			var hasNameContains = !string.IsNullOrEmpty(filter.NameContains);

			if (hasNameContains)
				terms.Add((DirectoryConditionJoin.And, FormatMethod("Name", "Contains", filter.NameContains!)));

			foreach (var column in filter.Columns)
			{
				if (FormatColumn(column) is not { } text)
					continue;

				terms.Add((column.Join, text));
			}

			if (filter.Regex is { } regex)
				terms.Add((DirectoryConditionJoin.And, FormatRegex(regex)));

			if (terms.Count is 0)
				return string.Empty;

			var builder = new StringBuilder();
			for (var index = 0; index < terms.Count; index++)
			{
				if (index > 0)
				{
					// NameContains applies to the whole chain, so the first column after it always ANDs.
					var join = index is 1 && hasNameContains ? DirectoryConditionJoin.And : terms[index].Join;
					builder.Append(join is DirectoryConditionJoin.Or ? " || " : " && ");
				}

				builder.Append(terms[index].Text);
			}

			return builder.ToString();
		}

		public static bool TryParse(string? text, out DirectoryFilterSpec filter, out DirectoryAddressError error)
		{
			filter = DirectoryFilterSpec.Empty;
			error = DirectoryAddressError.None;

			if (string.IsNullOrWhiteSpace(text))
			{
				error = DirectoryAddressError.MissingValue;
				return false;
			}

			try
			{
				var parser = new Parser(text);
				var node = parser.ParseExpression();
				parser.EnsureEnd();
				filter = BuildSpec(node);
				return true;
			}
			catch (AddressFilterException ex)
			{
				error = ex.Error;
				return false;
			}
		}

		private static string? FormatColumn(DirectoryColumnFilter column)
			=> column.Column switch
			{
				DirectoryColumnKind.Name => FormatName(column),
				DirectoryColumnKind.Type or DirectoryColumnKind.Extension or DirectoryColumnKind.Tags => FormatChoice(column),
				DirectoryColumnKind.Size => FormatRange("Size", column.MinSize, column.MaxSize, FormatSize),
				DirectoryColumnKind.ModifiedDate => FormatRange("ModifiedDate", column.MinDate, column.MaxDate, FormatDateValue),
				DirectoryColumnKind.CreatedDate => FormatRange("CreatedDate", column.MinDate, column.MaxDate, FormatDateValue),
				_ => null,
			};

		private static string? FormatName(DirectoryColumnFilter column)
		{
			// A per-condition regex has no LINQ equivalent and stays out of the address.
			if (column.Regex is not null)
				return null;

			var member = column.Scope is DirectoryNameScope.RawName ? "RawName" : "Name";

			switch (column.Operator)
			{
				case DirectoryTextOperator.Contains when !string.IsNullOrEmpty(column.Text):
					return FormatMethod(member, "Contains", column.Text!);

				case DirectoryTextOperator.NotContains when !string.IsNullOrEmpty(column.Text):
					return "!" + FormatMethod(member, "Contains", column.Text!);

				case DirectoryTextOperator.StartsWith when !string.IsNullOrEmpty(column.Text):
					return FormatMethod(member, "StartsWith", column.Text!);

				case DirectoryTextOperator.StartsWithNot when !string.IsNullOrEmpty(column.Text):
					return "!" + FormatMethod(member, "StartsWith", column.Text!);

				case DirectoryTextOperator.EndsWith when !string.IsNullOrEmpty(column.Text):
					return FormatMethod(member, "EndsWith", column.Text!);

				case DirectoryTextOperator.EndsWithNot when !string.IsNullOrEmpty(column.Text):
					return "!" + FormatMethod(member, "EndsWith", column.Text!);

				case DirectoryTextOperator.Equals when !string.IsNullOrEmpty(column.Text):
					return member + " == " + Quote(column.Text!);

				case DirectoryTextOperator.NotEquals when !string.IsNullOrEmpty(column.Text):
					return member + " != " + Quote(column.Text!);

				case DirectoryTextOperator.Wildcard when !string.IsNullOrEmpty(column.Text):
					return FormatMethod(member, "Wildcard", column.Text!);

				case DirectoryTextOperator.LengthGreaterThan when column.Length is { } greaterThan:
					return FormatLength(member, ">", greaterThan);

				case DirectoryTextOperator.LengthLessThan when column.Length is { } lessThan:
					return FormatLength(member, "<", lessThan);

				case DirectoryTextOperator.LengthEquals when column.Length is { } equals:
					return FormatLength(member, "==", equals);

				case DirectoryTextOperator.LengthNotEquals when column.Length is { } notEquals:
					return FormatLength(member, "!=", notEquals);

				default:
					return null;
			}
		}

		private static string? FormatChoice(DirectoryColumnFilter column)
		{
			var terms = new List<string>();

			foreach (var value in column.Values)
			{
				if (string.IsNullOrEmpty(value))
					continue;

				terms.Add(column.Column is DirectoryColumnKind.Tags
					? FormatMethod("Tags", "Contains", value)
					: ColumnMember(column.Column) + " == " + Quote(value));
			}

			if (column.IncludeEmpty)
			{
				terms.Add(column.Column is DirectoryColumnKind.Tags
					? "Tags.Count == 0"
					: ColumnMember(column.Column) + " == \"\"");
			}

			return terms.Count switch
			{
				0 => null,
				1 => terms[0],
				_ => "(" + string.Join(" || ", terms) + ")",
			};
		}

		private static string? FormatRange<T>(
			string member,
			T? min,
			T? max,
			Func<T, string> format)
			where T : struct
		{
			if (min is not { } minValue && max is not { } maxValue)
				return null;

			if (min is { } minOnly && max is { } maxOnly &&
				EqualityComparer<T>.Default.Equals(minOnly, maxOnly))
			{
				return member + " == " + format(minOnly);
			}

			var terms = new List<string>(2);

			if (min is { } minBound)
				terms.Add(member + " >= " + format(minBound));

			if (max is { } maxBound)
				terms.Add(member + " <= " + format(maxBound));

			return string.Join(" && ", terms);
		}

		private static string FormatRegex(DirectoryRegexOptions regex)
		{
			var target = regex.Target switch
			{
				DirectoryRegexTarget.DisplayName => "DisplayName",
				DirectoryRegexTarget.FullPath => "FullPath",
				_ => "RawName",
			};

			var flag = regex.IgnoreCase ? "i" : "c";
			return "Regex.IsMatch(" + target + ", " + Quote(regex.Pattern) + ", " + Quote(flag) + ")";
		}

		private static string FormatMethod(string member, string method, string value)
			=> member + "." + method + "(" + Quote(value) + ")";

		private static string FormatLength(string member, string comparison, int length)
			=> member + ".Length " + comparison + " " + length.ToString(CultureInfo.InvariantCulture);

		private static string ColumnMember(DirectoryColumnKind column)
			=> column switch
			{
				DirectoryColumnKind.Type => "Type",
				DirectoryColumnKind.Extension => "Extension",
				_ => "Tags",
			};

		private static string Quote(string value)
			=> "\"" + value.Replace("\"", "\"\"") + "\"";

		private static DirectoryFilterSpec BuildSpec(Node root)
		{
			var terms = new List<Term>();
			DirectoryRegexOptions? regex = null;
			var termCount = 0;

			if (root is GroupNode rootGroup)
			{
				var inner = new List<Term>();
				DirectoryRegexOptions? innerRegex = null;
				Flatten(rootGroup.Inner, DirectoryConditionJoin.And, inner, ref innerRegex, ref termCount);

				if (innerRegex is not null)
				{
					// Regex is applied to the whole projection, not to one branch of a chain.
					foreach (var term in inner)
					{
						if (term.Join is DirectoryConditionJoin.Or)
							throw new AddressFilterException(DirectoryAddressError.InvalidValue);
					}

					regex = innerRegex;
				}

				if (TryCollapseChoiceGroup(inner, out var collapsed))
					terms.Add(new Term(DirectoryConditionJoin.And, collapsed));
				else
					terms.AddRange(inner);
			}
			else
			{
				Flatten(root, DirectoryConditionJoin.And, terms, ref regex, ref termCount);
			}

			var columns = new List<DirectoryColumnFilter>(terms.Count);
			for (var index = 0; index < terms.Count; index++)
			{
				columns.Add(terms[index].Filter with
				{
					Join = index is 0 ? DirectoryConditionJoin.And : terms[index].Join,
				});
			}

			return new DirectoryFilterSpec
			{
				Columns = MergeAdjacentRanges(columns),
				Regex = regex,
			};
		}

		private static void Flatten(
			Node node,
			DirectoryConditionJoin join,
			List<Term> terms,
			ref DirectoryRegexOptions? regex,
			ref int termCount)
		{
			switch (node)
			{
				case AndNode and:
					Flatten(and.Left, join, terms, ref regex, ref termCount);
					Flatten(and.Right, DirectoryConditionJoin.And, terms, ref regex, ref termCount);
					break;

				case OrNode or:
					Flatten(or.Left, join, terms, ref regex, ref termCount);
					Flatten(or.Right, DirectoryConditionJoin.Or, terms, ref regex, ref termCount);
					break;

				case GroupNode group:
					if (ContainsRegex(group.Inner))
						throw new AddressFilterException(DirectoryAddressError.InvalidValue);

					var inner = new List<Term>();
					DirectoryRegexOptions? innerRegex = null;
					Flatten(group.Inner, DirectoryConditionJoin.And, inner, ref innerRegex, ref termCount);

					if (!TryCollapseChoiceGroup(inner, out var collapsed))
						throw new AddressFilterException(DirectoryAddressError.InvalidValue);

					AddTerm(join, collapsed, terms, ref termCount);
					break;

				case NotNode not:
					if (not.Inner is not AtomNode { Atom: ColumnAtom columnAtom })
						throw new AddressFilterException(DirectoryAddressError.InvalidOperator);

					AddTerm(join, Negate(columnAtom.Filter), terms, ref termCount);
					break;

				case AtomNode { Atom: RegexAtom regexAtom }:
					if (join is DirectoryConditionJoin.Or || regex is not null)
						throw new AddressFilterException(DirectoryAddressError.InvalidValue);

					regex = regexAtom.Regex;
					break;

				case AtomNode { Atom: ColumnAtom atom }:
					AddTerm(join, atom.Filter, terms, ref termCount);
					break;
			}
		}

		private static void AddTerm(
			DirectoryConditionJoin join,
			DirectoryColumnFilter filter,
			List<Term> terms,
			ref int termCount)
		{
			if (++termCount > MaxTerms)
				throw new AddressFilterException(DirectoryAddressError.TooLong);

			terms.Add(new Term(join, filter));
		}

		private static bool ContainsRegex(Node node)
			=> node switch
			{
				AtomNode { Atom: RegexAtom } => true,
				NotNode not => ContainsRegex(not.Inner),
				AndNode and => ContainsRegex(and.Left) || ContainsRegex(and.Right),
				OrNode or => ContainsRegex(or.Left) || ContainsRegex(or.Right),
				GroupNode group => ContainsRegex(group.Inner),
				_ => false,
			};

		private static DirectoryColumnFilter Negate(DirectoryColumnFilter filter)
		{
			if (filter.Column is not DirectoryColumnKind.Name)
				throw new AddressFilterException(DirectoryAddressError.InvalidOperator);

			var negated = filter.Operator switch
			{
				DirectoryTextOperator.Contains => DirectoryTextOperator.NotContains,
				DirectoryTextOperator.NotContains => DirectoryTextOperator.Contains,
				DirectoryTextOperator.StartsWith => DirectoryTextOperator.StartsWithNot,
				DirectoryTextOperator.StartsWithNot => DirectoryTextOperator.StartsWith,
				DirectoryTextOperator.EndsWith => DirectoryTextOperator.EndsWithNot,
				DirectoryTextOperator.EndsWithNot => DirectoryTextOperator.EndsWith,
				DirectoryTextOperator.Equals => DirectoryTextOperator.NotEquals,
				DirectoryTextOperator.NotEquals => DirectoryTextOperator.Equals,
				DirectoryTextOperator.LengthEquals => DirectoryTextOperator.LengthNotEquals,
				DirectoryTextOperator.LengthNotEquals => DirectoryTextOperator.LengthEquals,
				_ => throw new AddressFilterException(DirectoryAddressError.InvalidOperator),
			};

			return filter with { Operator = negated };
		}

		private static bool TryCollapseChoiceGroup(List<Term> terms, out DirectoryColumnFilter filter)
		{
			filter = null!;

			if (terms.Count is 0)
				return false;

			if (terms.Count is 1)
			{
				filter = terms[0].Filter;
				return true;
			}

			var column = terms[0].Filter.Column;
			if (column is not (DirectoryColumnKind.Type or DirectoryColumnKind.Extension or DirectoryColumnKind.Tags))
				return false;

			var values = new List<string>();
			var includeEmpty = false;

			for (var index = 0; index < terms.Count; index++)
			{
				var term = terms[index];

				if (index > 0 && term.Join is not DirectoryConditionJoin.Or)
					return false;

				if (term.Filter.Column != column || !IsChoiceOnly(term.Filter))
					return false;

				foreach (var value in term.Filter.Values)
				{
					if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
						values.Add(value);
				}

				includeEmpty |= term.Filter.IncludeEmpty;
			}

			filter = new DirectoryColumnFilter
			{
				Column = column,
				Values = values,
				IncludeEmpty = includeEmpty,
			};

			return true;
		}

		private static bool IsChoiceOnly(DirectoryColumnFilter filter)
			=> filter.Text is null &&
			   filter.Length is null &&
			   filter.Regex is null &&
			   filter.MinSize is null &&
			   filter.MaxSize is null &&
			   filter.MinDate is null &&
			   filter.MaxDate is null;

		private static List<DirectoryColumnFilter> MergeAdjacentRanges(List<DirectoryColumnFilter> columns)
		{
			if (columns.Count < 2)
				return columns;

			var merged = new List<DirectoryColumnFilter>(columns.Count);

			foreach (var column in columns)
			{
				if (column.Join is DirectoryConditionJoin.And &&
					merged.Count > 0 &&
					TryMergeRange(merged[^1], column, out var combined))
				{
					merged[^1] = combined;
					continue;
				}

				merged.Add(column);
			}

			return merged;
		}

		private static bool TryMergeRange(
			DirectoryColumnFilter previous,
			DirectoryColumnFilter current,
			out DirectoryColumnFilter combined)
		{
			combined = null!;

			if (previous.Column != current.Column)
				return false;

			switch (previous.Column)
			{
				case DirectoryColumnKind.Size when IsRangeOnly(previous) && IsRangeOnly(current):
					combined = previous with
					{
						MinSize = Max(previous.MinSize, current.MinSize),
						MaxSize = Min(previous.MaxSize, current.MaxSize),
					};

					return true;

				case DirectoryColumnKind.ModifiedDate or DirectoryColumnKind.CreatedDate
					when IsRangeOnly(previous) && IsRangeOnly(current):
					combined = previous with
					{
						MinDate = Max(previous.MinDate, current.MinDate),
						MaxDate = Min(previous.MaxDate, current.MaxDate),
					};

					return true;

				default:
					return false;
			}
		}

		private static bool IsRangeOnly(DirectoryColumnFilter filter)
			=> filter.Text is null &&
			   filter.Length is null &&
			   filter.Regex is null &&
			   filter.Values.Count is 0 &&
			   !filter.IncludeEmpty;

		private static long? Max(long? left, long? right)
			=> left is { } leftValue && right is { } rightValue
				? Math.Max(leftValue, rightValue)
				: left ?? right;

		private static long? Min(long? left, long? right)
			=> left is { } leftValue && right is { } rightValue
				? Math.Min(leftValue, rightValue)
				: left ?? right;

		private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
			=> left is { } leftValue && right is { } rightValue
				? (leftValue >= rightValue ? leftValue : rightValue)
				: left ?? right;

		private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right)
			=> left is { } leftValue && right is { } rightValue
				? (leftValue <= rightValue ? leftValue : rightValue)
				: left ?? right;

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

			var multiplier = UnitMultiplier(unit);
			if (multiplier == 0)
				return false;

			var result = number * multiplier;
			if (result > long.MaxValue)
				return false;

			bytes = (long)Math.Round(result, MidpointRounding.AwayFromZero);
			return true;
		}

		private static string FormatDate(DateTimeOffset value)
			=> value.ToString("O", CultureInfo.InvariantCulture);

		private static string FormatDateValue(DateTimeOffset value)
			=> Quote(FormatDate(value));

		private static double UnitMultiplier(string unit)
			=> unit.ToUpperInvariant() switch
			{
				"" or "B" => 1d,
				"KB" => 1024d,
				"MB" => 1024d * 1024d,
				"GB" => 1024d * 1024d * 1024d,
				"TB" => 1024d * 1024d * 1024d * 1024d,
				_ => 0d,
			};

		private readonly record struct Term(DirectoryConditionJoin Join, DirectoryColumnFilter Filter);

		private abstract record Node;

		private sealed record AtomNode(Atom Atom) : Node;

		private sealed record NotNode(Node Inner) : Node;

		private sealed record AndNode(Node Left, Node Right) : Node;

		private sealed record OrNode(Node Left, Node Right) : Node;

		private sealed record GroupNode(Node Inner) : Node;

		private abstract record Atom;

		private sealed record ColumnAtom(DirectoryColumnFilter Filter) : Atom;

		private sealed record RegexAtom(DirectoryRegexOptions Regex) : Atom;

		private sealed class AddressFilterException : Exception
		{
			public AddressFilterException(DirectoryAddressError error)
				: base(error.ToString())
			{
				Error = error;
			}

			public DirectoryAddressError Error { get; }
		}

		private enum MemberKind
		{
			Name,
			RawName,
			Type,
			Extension,
			Tags,
			Size,
			ModifiedDate,
			CreatedDate,
		}

		private sealed class Parser
		{
			private readonly List<Token> tokens = [];
			private int position;

			public Parser(string text)
			{
				Tokenize(text);
			}

			public Node ParseExpression()
				=> ParseOr();

			public void EnsureEnd()
			{
				if (Peek().Kind is not TokenKind.End)
					throw Invalid(DirectoryAddressError.InvalidValue);
			}

			private Node ParseOr()
			{
				var left = ParseAnd();

				while (Match(TokenKind.Or))
					left = new OrNode(left, ParseAnd());

				return left;
			}

			private Node ParseAnd()
			{
				var left = ParseUnary();

				while (Match(TokenKind.And))
					left = new AndNode(left, ParseUnary());

				return left;
			}

			private Node ParseUnary()
			{
				if (Match(TokenKind.Not))
					return new NotNode(ParseUnary());

				return ParsePrimary();
			}

			private Node ParsePrimary()
			{
				if (Match(TokenKind.OpenParen))
				{
					var inner = ParseExpression();
					Expect(TokenKind.CloseParen);
					return new GroupNode(inner);
				}

				return ParseComparison();
			}

			private Node ParseComparison()
			{
				var memberToken = Expect(TokenKind.Identifier);

				if (memberToken.Text.Equals("Regex", StringComparison.OrdinalIgnoreCase))
					return ParseRegexCall();

				var kind = ResolveMember(memberToken.Text);

				if (Match(TokenKind.Dot))
				{
					var accessor = Expect(TokenKind.Identifier).Text.ToLowerInvariant();

					if (kind is MemberKind.Name or MemberKind.RawName)
					{
						if (accessor is "length")
							return ParseLength(kind);

						if (accessor is "contains" or "startswith" or "endswith" or "equals" or "wildcard")
							return ParseTextMethod(kind, accessor);
					}

					if (kind is MemberKind.Tags && accessor is "contains")
						return ParseTagsContains();

					if (kind is MemberKind.Tags && accessor is "count")
						return ParseTagsCount();

					throw Invalid(DirectoryAddressError.InvalidOperator);
				}

				var op = ReadComparisonOperator();

				return kind switch
				{
					MemberKind.Name or MemberKind.RawName => ParseTextComparison(kind, op),
					MemberKind.Type or MemberKind.Extension => ParseChoiceComparison(kind, op),
					MemberKind.Size => ParseSizeComparison(op),
					MemberKind.ModifiedDate or MemberKind.CreatedDate => ParseDateComparison(kind, op),
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};
			}

			private Node ParseTextMethod(MemberKind kind, string accessor)
			{
				Expect(TokenKind.OpenParen);
				var value = Expect(TokenKind.String).Text;
				Expect(TokenKind.CloseParen);

				if (value.Length is 0)
					throw Invalid(DirectoryAddressError.MissingValue);

				var textOperator = accessor switch
				{
					"contains" => DirectoryTextOperator.Contains,
					"startswith" => DirectoryTextOperator.StartsWith,
					"endswith" => DirectoryTextOperator.EndsWith,
					"equals" => DirectoryTextOperator.Equals,
					"wildcard" => DirectoryTextOperator.Wildcard,
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};

				return new AtomNode(new ColumnAtom(new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Scope = ToScope(kind),
					Operator = textOperator,
					Text = value,
				}));
			}

			private Node ParseTextComparison(MemberKind kind, ComparisonOperator op)
			{
				var value = Expect(TokenKind.String).Text;

				if (value.Length is 0)
					throw Invalid(DirectoryAddressError.MissingValue);

				var textOperator = op switch
				{
					ComparisonOperator.Equal => DirectoryTextOperator.Equals,
					ComparisonOperator.NotEqual => DirectoryTextOperator.NotEquals,
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};

				return new AtomNode(new ColumnAtom(new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Scope = ToScope(kind),
					Operator = textOperator,
					Text = value,
				}));
			}

			private Node ParseLength(MemberKind kind)
			{
				var op = ReadComparisonOperator();
				var token = Expect(TokenKind.Number);

				if (token.HasDecimal || token.Unit.Length > 0 || token.Number < 0 || token.Number > int.MaxValue)
					throw Invalid(DirectoryAddressError.InvalidValue);

				var lengthOperator = op switch
				{
					ComparisonOperator.Equal => DirectoryTextOperator.LengthEquals,
					ComparisonOperator.NotEqual => DirectoryTextOperator.LengthNotEquals,
					ComparisonOperator.GreaterThan => DirectoryTextOperator.LengthGreaterThan,
					ComparisonOperator.LessThan => DirectoryTextOperator.LengthLessThan,
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};

				return new AtomNode(new ColumnAtom(new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Name,
					Scope = ToScope(kind),
					Operator = lengthOperator,
					Length = (int)token.Number,
				}));
			}

			private Node ParseChoiceComparison(MemberKind kind, ComparisonOperator op)
			{
				if (op is not ComparisonOperator.Equal)
					throw Invalid(DirectoryAddressError.InvalidOperator);

				var value = Expect(TokenKind.String).Text;
				var column = kind is MemberKind.Type ? DirectoryColumnKind.Type : DirectoryColumnKind.Extension;

				return new AtomNode(new ColumnAtom(value.Length is 0
					? new DirectoryColumnFilter { Column = column, IncludeEmpty = true }
					: new DirectoryColumnFilter { Column = column, Values = [value] }));
			}

			private Node ParseTagsContains()
			{
				Expect(TokenKind.OpenParen);
				var value = Expect(TokenKind.String).Text;
				Expect(TokenKind.CloseParen);

				if (value.Length is 0)
					throw Invalid(DirectoryAddressError.MissingValue);

				return new AtomNode(new ColumnAtom(new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Tags,
					Values = [value],
				}));
			}

			private Node ParseTagsCount()
			{
				var op = ReadComparisonOperator();
				var token = Expect(TokenKind.Number);

				if (op is not ComparisonOperator.Equal || token.Number != 0 || token.Unit.Length > 0)
					throw Invalid(DirectoryAddressError.InvalidOperator);

				return new AtomNode(new ColumnAtom(new DirectoryColumnFilter
				{
					Column = DirectoryColumnKind.Tags,
					IncludeEmpty = true,
				}));
			}

			private Node ParseSizeComparison(ComparisonOperator op)
			{
				if (op is not (ComparisonOperator.Equal or ComparisonOperator.GreaterOrEqual or ComparisonOperator.LessOrEqual))
					throw Invalid(DirectoryAddressError.InvalidOperator);

				var token = Expect(TokenKind.Number);
				var value = token.Number;

				var filter = op switch
				{
					ComparisonOperator.Equal => new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Size,
						MinSize = value,
						MaxSize = value,
					},
					ComparisonOperator.GreaterOrEqual => new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Size,
						MinSize = value,
					},
					ComparisonOperator.LessOrEqual => new DirectoryColumnFilter
					{
						Column = DirectoryColumnKind.Size,
						MaxSize = value,
					},
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};

				return new AtomNode(new ColumnAtom(filter));
			}

			private Node ParseDateComparison(MemberKind kind, ComparisonOperator op)
			{
				if (op is not (ComparisonOperator.Equal or ComparisonOperator.GreaterOrEqual or ComparisonOperator.LessOrEqual))
					throw Invalid(DirectoryAddressError.InvalidOperator);

				var raw = Expect(TokenKind.String).Text;
				if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
					throw Invalid(DirectoryAddressError.InvalidValue);

				var column = kind is MemberKind.ModifiedDate
					? DirectoryColumnKind.ModifiedDate
					: DirectoryColumnKind.CreatedDate;

				var filter = op switch
				{
					ComparisonOperator.Equal => new DirectoryColumnFilter
					{
						Column = column,
						MinDate = value,
						MaxDate = value,
					},
					ComparisonOperator.GreaterOrEqual => new DirectoryColumnFilter
					{
						Column = column,
						MinDate = value,
					},
					ComparisonOperator.LessOrEqual => new DirectoryColumnFilter
					{
						Column = column,
						MaxDate = value,
					},
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};

				return new AtomNode(new ColumnAtom(filter));
			}

			private Node ParseRegexCall()
			{
				Expect(TokenKind.Dot);

				var method = Expect(TokenKind.Identifier).Text;
				if (!method.Equals("IsMatch", StringComparison.OrdinalIgnoreCase))
					throw Invalid(DirectoryAddressError.InvalidOperator);

				Expect(TokenKind.OpenParen);

				var target = ResolveRegexTarget(Expect(TokenKind.Identifier).Text);
				Expect(TokenKind.Comma);

				var pattern = Expect(TokenKind.String).Text;
				if (pattern.Length > MaxRegexLength)
					throw Invalid(DirectoryAddressError.InvalidValue);

				var ignoreCase = true;

				if (Match(TokenKind.Comma))
				{
					var flags = Expect(TokenKind.String).Text;
					foreach (var flag in flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					{
						switch (flag.ToLowerInvariant())
						{
							case "i":
								ignoreCase = true;
								break;

							case "c":
								ignoreCase = false;
								break;

							default:
								throw Invalid(DirectoryAddressError.InvalidValue);
						}
					}
				}

				Expect(TokenKind.CloseParen);

				return new AtomNode(new RegexAtom(new DirectoryRegexOptions(pattern, target, ignoreCase)));
			}

			private ComparisonOperator ReadComparisonOperator()
			{
				var token = Next();

				return token.Kind switch
				{
					TokenKind.Equals => ComparisonOperator.Equal,
					TokenKind.NotEquals => ComparisonOperator.NotEqual,
					TokenKind.GreaterThan => ComparisonOperator.GreaterThan,
					TokenKind.GreaterOrEqual => ComparisonOperator.GreaterOrEqual,
					TokenKind.LessThan => ComparisonOperator.LessThan,
					TokenKind.LessOrEqual => ComparisonOperator.LessOrEqual,
					_ => throw Invalid(DirectoryAddressError.InvalidOperator),
				};
			}

			private static MemberKind ResolveMember(string name)
				=> name.ToLowerInvariant() switch
				{
					"name" => MemberKind.Name,
					"rawname" => MemberKind.RawName,
					"type" => MemberKind.Type,
					"extension" or "ext" => MemberKind.Extension,
					"tags" or "tag" => MemberKind.Tags,
					"size" => MemberKind.Size,
					"modifieddate" or "modified" => MemberKind.ModifiedDate,
					"createddate" or "created" => MemberKind.CreatedDate,
					_ => throw Invalid(DirectoryAddressError.InvalidValue),
				};

			private static DirectoryNameScope ToScope(MemberKind kind)
				=> kind is MemberKind.RawName ? DirectoryNameScope.RawName : DirectoryNameScope.DisplayName;

			private static DirectoryRegexTarget ResolveRegexTarget(string target)
				=> target.ToLowerInvariant() switch
				{
					"rawname" => DirectoryRegexTarget.RawName,
					"displayname" => DirectoryRegexTarget.DisplayName,
					"fullpath" => DirectoryRegexTarget.FullPath,
					_ => throw Invalid(DirectoryAddressError.InvalidValue),
				};

			private static AddressFilterException Invalid(DirectoryAddressError error)
				=> new(error);

			private bool Match(TokenKind kind)
			{
				if (Peek().Kind != kind)
					return false;

				position++;
				return true;
			}

			private Token Expect(TokenKind kind)
			{
				var token = Next();

				if (token.Kind != kind)
					throw Invalid(DirectoryAddressError.InvalidValue);

				return token;
			}

			private Token Peek()
				=> tokens[position];

			private Token Next()
				=> tokens[position++];

			private void Tokenize(string text)
			{
				var index = 0;

				while (index < text.Length)
				{
					var current = text[index];

					if (char.IsWhiteSpace(current))
					{
						index++;
						continue;
					}

					if (char.IsLetter(current) || current is '_')
					{
						var start = index;

						while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_'))
							index++;

						tokens.Add(new Token(TokenKind.Identifier, text[start..index]));
						continue;
					}

					if (char.IsDigit(current))
					{
						index = ReadNumber(text, index);
						continue;
					}

					if (current is '"')
					{
						index = ReadString(text, index);
						continue;
					}

					switch (current)
					{
						case '&' when index + 1 < text.Length && text[index + 1] is '&':
							tokens.Add(new Token(TokenKind.And));
							index += 2;
							continue;

						case '|' when index + 1 < text.Length && text[index + 1] is '|':
							tokens.Add(new Token(TokenKind.Or));
							index += 2;
							continue;

						case '!' when index + 1 < text.Length && text[index + 1] is '=':
							tokens.Add(new Token(TokenKind.NotEquals));
							index += 2;
							continue;

						case '!':
							tokens.Add(new Token(TokenKind.Not));
							index++;
							continue;

						case '=' when index + 1 < text.Length && text[index + 1] is '=':
							tokens.Add(new Token(TokenKind.Equals));
							index += 2;
							continue;

						case '>' when index + 1 < text.Length && text[index + 1] is '=':
							tokens.Add(new Token(TokenKind.GreaterOrEqual));
							index += 2;
							continue;

						case '<' when index + 1 < text.Length && text[index + 1] is '=':
							tokens.Add(new Token(TokenKind.LessOrEqual));
							index += 2;
							continue;

						case '>':
							tokens.Add(new Token(TokenKind.GreaterThan));
							index++;
							continue;

						case '<':
							tokens.Add(new Token(TokenKind.LessThan));
							index++;
							continue;

						case '(':
							tokens.Add(new Token(TokenKind.OpenParen));
							index++;
							continue;

						case ')':
							tokens.Add(new Token(TokenKind.CloseParen));
							index++;
							continue;

						case '.':
							tokens.Add(new Token(TokenKind.Dot));
							index++;
							continue;

						case ',':
							tokens.Add(new Token(TokenKind.Comma));
							index++;
							continue;

						default:
							throw Invalid(DirectoryAddressError.InvalidValue);
					}
				}

				tokens.Add(new Token(TokenKind.End));
			}

			private int ReadNumber(string text, int index)
			{
				var start = index;
				var hasDecimal = false;

				while (index < text.Length && char.IsDigit(text[index]))
					index++;

				if (index < text.Length && text[index] is '.' && index + 1 < text.Length && char.IsDigit(text[index + 1]))
				{
					hasDecimal = true;
					index++;

					while (index < text.Length && char.IsDigit(text[index]))
						index++;
				}

				var numberText = text[start..index];
				var unitStart = index;

				while (index < text.Length && char.IsLetter(text[index]))
					index++;

				var unit = text[unitStart..index];
				var multiplier = UnitMultiplier(unit);

				if (multiplier == 0)
					throw Invalid(DirectoryAddressError.InvalidValue);

				if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
					throw Invalid(DirectoryAddressError.InvalidValue);

				var value = number * multiplier;
				if (double.IsNaN(value) || double.IsInfinity(value) || value > long.MaxValue)
					throw Invalid(DirectoryAddressError.InvalidValue);

				tokens.Add(new Token(
					TokenKind.Number,
					numberText,
					(long)Math.Round(value, MidpointRounding.AwayFromZero),
					unit,
					hasDecimal));

				return index;
			}

			private int ReadString(string text, int index)
			{
				index++;
				var builder = new StringBuilder();
				var closed = false;

				while (index < text.Length)
				{
					if (text[index] is '"')
					{
						if (index + 1 < text.Length && text[index + 1] is '"')
						{
							builder.Append('"');
							index += 2;
							continue;
						}

						index++;
						closed = true;
						break;
					}

					builder.Append(text[index++]);
				}

				if (!closed)
					throw Invalid(DirectoryAddressError.InvalidQuotes);

				tokens.Add(new Token(TokenKind.String, builder.ToString()));
				return index;
			}
		}

		private enum TokenKind
		{
			Identifier,
			String,
			Number,
			And,
			Or,
			Not,
			Equals,
			NotEquals,
			GreaterThan,
			GreaterOrEqual,
			LessThan,
			LessOrEqual,
			OpenParen,
			CloseParen,
			Dot,
			Comma,
			End,
		}

		private enum ComparisonOperator
		{
			Equal,
			NotEqual,
			GreaterThan,
			GreaterOrEqual,
			LessThan,
			LessOrEqual,
		}

		private readonly record struct Token(
			TokenKind Kind,
			string Text = "",
			long Number = 0,
			string Unit = "",
			bool HasDecimal = false);
	}
}
