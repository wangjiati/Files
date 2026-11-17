// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Parses the LINQ-style filter expressions typed in the address bar, for example
	/// <c>Name.Contains("report") &amp;&amp; Size &gt; 1MB</c> or <c>ModifiedDate &gt; "2026-01-01"</c>.
	/// The parsed tree is interpreted directly so the filter stays Native AOT compatible.
	/// </summary>
	public static class LinqFilterExpression
	{
		public static bool TryParse(string? text, out Func<DirectoryItemFilterSnapshot, bool>? predicate, out string? error)
		{
			predicate = null;
			error = null;

			if (string.IsNullOrWhiteSpace(text))
			{
				error = "empty";
				return false;
			}

			try
			{
				var parser = new Parser(text);
				var node = parser.ParseExpression();
				parser.EnsureEnd();
				predicate = node.Evaluate;
				return true;
			}
			catch (FormatException ex)
			{
				error = ex.Message;
				return false;
			}
			catch (InvalidOperationException ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private enum Member
		{
			DisplayName,
			RawName,
			FullPath,
			Extension,
			ItemType,
			SizeBytes,
			ModifiedUtc,
			CreatedUtc,
			IsFolder,
		}

		private abstract record Node
		{
			public abstract bool Evaluate(DirectoryItemFilterSnapshot item);
		}

		private sealed record AndNode(Node Left, Node Right) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
				=> Left.Evaluate(item) && Right.Evaluate(item);
		}

		private sealed record OrNode(Node Left, Node Right) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
				=> Left.Evaluate(item) || Right.Evaluate(item);
		}

		private sealed record NotNode(Node Inner) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
				=> !Inner.Evaluate(item);
		}

		private sealed record MethodNode(Member Member, StringMethod Method, string Text) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
			{
				var value = GetString(item, Member);

				return Method switch
				{
					StringMethod.Contains => value is not null && value.Contains(Text, StringComparison.OrdinalIgnoreCase),
					StringMethod.StartsWith => value is not null && value.StartsWith(Text, StringComparison.OrdinalIgnoreCase),
					StringMethod.EndsWith => value is not null && value.EndsWith(Text, StringComparison.OrdinalIgnoreCase),
					_ => string.Equals(value, Text, StringComparison.OrdinalIgnoreCase),
				};
			}
		}

		private sealed record StringComparisonNode(Member Member, bool Equal, string Text) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
				=> string.Equals(GetString(item, Member), Text, StringComparison.OrdinalIgnoreCase) == Equal;
		}

		private sealed record NumberComparisonNode(Member Member, ComparisonOp Op, long Value) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
			{
				var value = GetNumber(item, Member);

				return value is not null && Compare(value.Value, Value, Op);
			}
		}

		private sealed record DateComparisonNode(Member Member, ComparisonOp Op, DateTimeOffset Value) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
			{
				var value = GetDate(item, Member);

				return value is not null && Compare(value.Value, Value, Op);
			}
		}

		private sealed record BooleanNode(Member Member, bool Equal, bool Value) : Node
		{
			public override bool Evaluate(DirectoryItemFilterSnapshot item)
				=> GetBoolean(item, Member) == Value == Equal;
		}

		private enum StringMethod
		{
			Contains,
			StartsWith,
			EndsWith,
			Equals,
		}

		private enum ComparisonOp
		{
			Equal,
			NotEqual,
			GreaterThan,
			GreaterOrEqual,
			LessThan,
			LessOrEqual,
		}

		private static string? GetString(DirectoryItemFilterSnapshot item, Member member)
			=> member switch
			{
				Member.DisplayName => item.DisplayName,
				Member.RawName => item.RawName,
				Member.FullPath => item.FullPath,
				Member.Extension => item.Extension,
				Member.ItemType => item.ItemType,
				_ => null,
			};

		private static long? GetNumber(DirectoryItemFilterSnapshot item, Member member)
			=> member is Member.SizeBytes ? item.SizeBytes : null;

		private static DateTimeOffset? GetDate(DirectoryItemFilterSnapshot item, Member member)
			=> member switch
			{
				Member.ModifiedUtc => item.ModifiedUtc,
				Member.CreatedUtc => item.CreatedUtc,
				_ => null,
			};

		private static bool GetBoolean(DirectoryItemFilterSnapshot item, Member member)
			=> member is Member.IsFolder && item.IsFolder;

		private static bool Compare(long left, long right, ComparisonOp op)
			=> op switch
			{
				ComparisonOp.Equal => left == right,
				ComparisonOp.NotEqual => left != right,
				ComparisonOp.GreaterThan => left > right,
				ComparisonOp.GreaterOrEqual => left >= right,
				ComparisonOp.LessThan => left < right,
				_ => left <= right,
			};

		private static bool Compare(DateTimeOffset left, DateTimeOffset right, ComparisonOp op)
			=> op switch
			{
				ComparisonOp.Equal => left == right,
				ComparisonOp.NotEqual => left != right,
				ComparisonOp.GreaterThan => left > right,
				ComparisonOp.GreaterOrEqual => left >= right,
				ComparisonOp.LessThan => left < right,
				_ => left <= right,
			};

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
					throw new FormatException($"Unexpected token '{Peek().Text}'.");
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
					return inner;
				}

				return ParseComparison();
			}

			private Node ParseComparison()
			{
				var member = ParseMember();

				if (Match(TokenKind.Dot))
					return ParseMethodCallCurrent(member);

				var op = ReadComparisonOperator();
				var token = Next();

				return BuildComparison(member, op, token);
			}


			private Node ParseMethodCallCurrent(Member member)
			{
				var methodToken = Expect(TokenKind.Identifier);
				Expect(TokenKind.OpenParen);
				var argument = Expect(TokenKind.String).Text;
				Expect(TokenKind.CloseParen);

				var method = methodToken.Text switch
				{
					"Contains" => StringMethod.Contains,
					"StartsWith" => StringMethod.StartsWith,
					"EndsWith" => StringMethod.EndsWith,
					"Equals" => StringMethod.Equals,
					_ => throw new InvalidOperationException($"Unsupported method '{methodToken.Text}'."),
				};

				return new MethodNode(member, method, argument);
			}

			private Member ParseMember()
			{
				var token = Expect(TokenKind.Identifier);

				return token.Text switch
				{
					"Name" or nameof(Member.DisplayName) => Member.DisplayName,
					nameof(Member.RawName) => Member.RawName,
					"Path" or nameof(Member.FullPath) => Member.FullPath,
					nameof(Member.Extension) => Member.Extension,
					"Type" or nameof(Member.ItemType) => Member.ItemType,
					"Size" or nameof(Member.SizeBytes) => Member.SizeBytes,
					"ModifiedDate" or nameof(Member.ModifiedUtc) => Member.ModifiedUtc,
					"CreatedDate" or nameof(Member.CreatedUtc) => Member.CreatedUtc,
					nameof(Member.IsFolder) => Member.IsFolder,
					_ => throw new InvalidOperationException($"Unknown member '{token.Text}'."),
				};
			}

			private Node BuildComparison(Member member, ComparisonOp op, Token token)
			{
				return member switch
				{
					Member.DisplayName or Member.RawName or Member.FullPath or Member.Extension or Member.ItemType
						=> BuildStringComparison(member, op, token),
					Member.SizeBytes => new NumberComparisonNode(member, op, ReadNumber(token)),
					Member.ModifiedUtc or Member.CreatedUtc => new DateComparisonNode(member, op, ReadDate(token)),
					Member.IsFolder => new BooleanNode(member, op is ComparisonOp.Equal, ReadBoolean(token)),
					_ => throw new FormatException($"Unsupported member '{member}'."),
				};
			}

			private static Node BuildStringComparison(Member member, ComparisonOp op, Token token)
			{
				if (op is not (ComparisonOp.Equal or ComparisonOp.NotEqual))
					throw new InvalidOperationException("Only == and != are supported for text members.");

				if (token.Kind is not TokenKind.String)
					throw new FormatException($"Invalid text literal '{token.Text}'.");

				return new StringComparisonNode(member, op is ComparisonOp.Equal, token.Text);
			}

			private static long ReadNumber(Token token)
			{
				if (token.Kind is not TokenKind.Number)
					throw new FormatException($"Invalid number literal '{token.Text}'.");

				return token.Number;
			}

			private static DateTimeOffset ReadDate(Token token)
			{
				if (token.Kind is not TokenKind.String || !DateTimeOffset.TryParse(token.Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value))
					throw new FormatException($"Invalid date literal '{token.Text}'.");

				return value;
			}

			private static bool ReadBoolean(Token token)
			{
				if (token.Kind is TokenKind.Identifier && bool.TryParse(token.Text, out var value))
					return value;

				throw new FormatException($"Invalid boolean literal '{token.Text}'.");
			}

			private ComparisonOp ReadComparisonOperator()
			{
				var token = Next();

				return token.Kind switch
				{
					TokenKind.EqualsEquals => ComparisonOp.Equal,
					TokenKind.NotEquals => ComparisonOp.NotEqual,
					TokenKind.GreaterThan => ComparisonOp.GreaterThan,
					TokenKind.GreaterOrEqual => ComparisonOp.GreaterOrEqual,
					TokenKind.LessThan => ComparisonOp.LessThan,
					TokenKind.LessOrEqual => ComparisonOp.LessOrEqual,
					_ => throw new FormatException($"Expected a comparison operator, found '{token.Text}'."),
				};
			}

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
					throw new FormatException($"Expected {kind}, found '{token.Text}'.");

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
						var start = index;
						while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '.'))
							index++;

						var numberText = text[start..index];
						var unit = new StringBuilder();
						while (index < text.Length && char.IsLetter(text[index]))
							unit.Append(text[index++]);

						if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
							throw new FormatException($"Invalid number '{numberText}'.");

						tokens.Add(new Token(TokenKind.Number, numberText, (long)(number * UnitMultiplier(unit.ToString()))));
						continue;
					}

					if (current is '"')
					{
						index++;
						var builder = new StringBuilder();
						while (index < text.Length && text[index] is not '"')
						{
							if (text[index] is '\\' && index + 1 < text.Length)
								index++;

							builder.Append(text[index++]);
						}

						if (index >= text.Length)
							throw new FormatException("Unterminated string literal.");

						index++;
						tokens.Add(new Token(TokenKind.String, builder.ToString()));
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
							tokens.Add(new Token(TokenKind.EqualsEquals));
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
						default:
							throw new FormatException($"Unexpected character '{current}'.");
					}
				}

				tokens.Add(new Token(TokenKind.End, "<end>"));
			}

			private static double UnitMultiplier(string unit)
				=> unit.ToUpperInvariant() switch
				{
					"" or "B" => 1d,
					"KB" => 1024d,
					"MB" => 1024d * 1024d,
					"GB" => 1024d * 1024d * 1024d,
					"TB" => 1024d * 1024d * 1024d * 1024d,
					_ => throw new FormatException($"Unknown size unit '{unit}'."),
				};
		}

		private enum TokenKind
		{
			Identifier,
			String,
			Number,
			And,
			Or,
			Not,
			EqualsEquals,
			NotEquals,
			GreaterThan,
			GreaterOrEqual,
			LessThan,
			LessOrEqual,
			OpenParen,
			CloseParen,
			Dot,
			End,
		}

		private readonly record struct Token(TokenKind Kind, string Text = "", long Number = 0);
	}
}
