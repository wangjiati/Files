// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Expands batch rename variables (counters, date/time tokens and the folder name) in a rename pattern.
	/// </summary>
	public sealed class RenameVariableExpander
	{
		private static readonly TimeSpan PatternMatchTimeout = TimeSpan.FromMilliseconds(250);

		private static readonly Regex BracedVariableRegex = new(@"\$\{([^}]*)\}", RegexOptions.Compiled, PatternMatchTimeout);

		private static readonly Regex DateTokenRegex = new(@"\$[A-Za-z]+", RegexOptions.Compiled, PatternMatchTimeout);

		// Case-sensitive on purpose: $MM (month) and $mm (minutes) differ only by case.
		private static readonly Dictionary<string, Func<DateTimeOffset, string>> DateTokenFormatters = new()
		{
			["YYYY"] = static date => date.Year.ToString(CultureInfo.InvariantCulture),
			["YY"] = static date => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
			["Y"] = static date => (date.Year % 10).ToString(CultureInfo.InvariantCulture),
			["MMMM"] = static date => date.ToString("MMMM", CultureInfo.CurrentCulture),
			["MMM"] = static date => date.ToString("MMM", CultureInfo.CurrentCulture),
			["MM"] = static date => date.Month.ToString("00", CultureInfo.InvariantCulture),
			["M"] = static date => date.Month.ToString(CultureInfo.InvariantCulture),
			["DDDD"] = static date => date.ToString("dddd", CultureInfo.CurrentCulture),
			["DDD"] = static date => date.ToString("ddd", CultureInfo.CurrentCulture),
			["DD"] = static date => date.Day.ToString("00", CultureInfo.InvariantCulture),
			["D"] = static date => date.Day.ToString(CultureInfo.InvariantCulture),
			["hh"] = static date => date.Hour.ToString("00", CultureInfo.InvariantCulture),
			["h"] = static date => date.Hour.ToString(CultureInfo.InvariantCulture),
			["mm"] = static date => date.Minute.ToString("00", CultureInfo.InvariantCulture),
			["m"] = static date => date.Minute.ToString(CultureInfo.InvariantCulture),
			["ss"] = static date => date.Second.ToString("00", CultureInfo.InvariantCulture),
			["s"] = static date => date.Second.ToString(CultureInfo.InvariantCulture),
			["fff"] = static date => date.Millisecond.ToString("000", CultureInfo.InvariantCulture),
			["ff"] = static date => (date.Millisecond / 10).ToString("00", CultureInfo.InvariantCulture),
			["f"] = static date => (date.Millisecond / 100).ToString(CultureInfo.InvariantCulture),
		};

		private readonly PatternSegment[] segments;
		private readonly CounterState[] counters;

		private RenameVariableExpander(PatternSegment[] segments, CounterState[] counters)
		{
			this.segments = segments;
			this.counters = counters;
		}

		/// <summary>
		/// Parses the pattern once; the returned expander keeps one counter per occurrence, in order of appearance, and advances them on every <see cref="Expand"/> call.
		/// </summary>
		public static RenameVariableExpander Create(string pattern)
		{
			ArgumentNullException.ThrowIfNull(pattern);

			List<PatternSegment> segments = [];
			List<CounterState> counters = [];
			int position = 0;

			foreach (Match match in BracedVariableRegex.Matches(pattern))
			{
				if (match.Index > position)
					AddTextSegments(segments, pattern[position..match.Index]);

				position = match.Index + match.Length;

				string content = match.Groups[1].Value;
				if (content.Length == 0)
				{
					AddCounterSegment(segments, counters, start: 0, increment: 1, padding: 1);
				}
				else if (content == "folder")
				{
					segments.Add(PatternSegment.FolderName());
				}
				else if (TryParseCounterOptions(content, out int start, out int increment, out int padding))
				{
					AddCounterSegment(segments, counters, start, increment, padding);
				}
				else
				{
					// Malformed variable: keep it literal.
					segments.Add(PatternSegment.Literal(match.Value));
				}
			}

			if (position < pattern.Length)
				AddTextSegments(segments, pattern[position..]);

			return new RenameVariableExpander([.. segments], [.. counters]);
		}

		/// <summary>
		/// Renders the pattern for the given item and advances every counter by its increment.
		/// </summary>
		public string Expand(VariableContext context)
		{
			StringBuilder builder = new();

			foreach (var segment in segments)
			{
				switch (segment.Kind)
				{
					case PatternSegmentKind.Literal:
						builder.Append(segment.Text);
						break;

					case PatternSegmentKind.FolderName:
						builder.Append(context.FolderName);
						break;

					case PatternSegmentKind.DateToken:
						builder.Append(DateTokenFormatters[segment.DateToken](GetContextDate(context)));
						break;

					case PatternSegmentKind.Counter:
						CounterState counter = counters[segment.CounterIndex];
						builder.Append(counter.Value.ToString(CultureInfo.InvariantCulture).PadLeft(counter.Padding, '0'));
						counter.Value += counter.Increment;
						break;
				}
			}

			return builder.ToString();
		}

		private static DateTimeOffset GetContextDate(VariableContext context)
		{
			return context.Source == FileDateSource.Created ? context.Created : context.Modified;
		}

		// Splits literal text around recognized $tokens; unrecognized runs stay literal.
		private static void AddTextSegments(List<PatternSegment> segments, string text)
		{
			int position = 0;

			foreach (Match match in DateTokenRegex.Matches(text))
			{
				string token = match.Value[1..];
				if (!DateTokenFormatters.ContainsKey(token))
					continue;

				if (match.Index > position)
					segments.Add(PatternSegment.Literal(text[position..match.Index]));

				segments.Add(PatternSegment.Date(token));
				position = match.Index + match.Length;
			}

			if (position < text.Length)
				segments.Add(PatternSegment.Literal(text[position..]));
		}

		private static void AddCounterSegment(List<PatternSegment> segments, List<CounterState> counters, int start, int increment, int padding)
		{
			counters.Add(new CounterState(start, increment, padding));
			segments.Add(PatternSegment.Counter(counters.Count - 1));
		}

		private static bool TryParseCounterOptions(string content, out int start, out int increment, out int padding)
		{
			start = 0;
			increment = 1;
			padding = 1;

			bool hasStart = false;
			bool hasIncrement = false;
			bool hasPadding = false;

			foreach (string part in content.Split(';'))
			{
				int separatorIndex = part.IndexOf('=');
				if (separatorIndex < 0)
					return false;

				string key = part[..separatorIndex];
				string value = part[(separatorIndex + 1)..];

				if (key == "start" && !hasStart && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsedStart))
				{
					hasStart = true;
					start = parsedStart;
				}
				else if (key == "increment" && !hasIncrement && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsedIncrement))
				{
					hasIncrement = true;
					increment = parsedIncrement;
				}
				else if (key == "padding" && !hasPadding && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsedPadding) && parsedPadding >= 0)
				{
					hasPadding = true;
					padding = parsedPadding;
				}
				else
				{
					return false;
				}
			}

			return true;
		}

		private enum PatternSegmentKind
		{
			Literal,
			Counter,
			DateToken,
			FolderName,
		}

		private readonly record struct PatternSegment(PatternSegmentKind Kind, string Text, string DateToken, int CounterIndex)
		{
			public static PatternSegment Literal(string text) => new(PatternSegmentKind.Literal, text, string.Empty, -1);

			public static PatternSegment FolderName() => new(PatternSegmentKind.FolderName, string.Empty, string.Empty, -1);

			public static PatternSegment Date(string token) => new(PatternSegmentKind.DateToken, string.Empty, token, -1);

			public static PatternSegment Counter(int counterIndex) => new(PatternSegmentKind.Counter, string.Empty, string.Empty, counterIndex);
		}

		private sealed class CounterState(int start, int increment, int padding)
		{
			public int Value { get; set; } = start;

			public int Increment { get; set; } = increment;

			public int Padding { get; set; } = padding;
		}
	}

	/// <summary>
	/// The values the rename variables expand against for a single item.
	/// </summary>
	public readonly record struct VariableContext(DateTimeOffset Created, DateTimeOffset Modified, FileDateSource Source, string FolderName);
}
