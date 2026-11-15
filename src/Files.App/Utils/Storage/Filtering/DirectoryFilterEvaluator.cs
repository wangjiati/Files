// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Evaluates a <see cref="DirectoryFilterSpec"/> against scalar snapshots in a single pass,
	/// returning matching item IDs in their original order.
	/// </summary>
	public static class DirectoryFilterEvaluator
	{
		/// <summary>
		/// Overall budget for regex matching across one projection. A catastrophic expression
		/// is abandoned instead of freezing the directory.
		/// </summary>
		private static readonly TimeSpan RegexTotalBudget = TimeSpan.FromSeconds(2);

		/// <summary>
		/// Wildcard patterns repeat per item and per projection, so their translated expressions are shared.
		/// </summary>
		private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new(StringComparer.Ordinal);

		public static IReadOnlyList<long> Evaluate(IReadOnlyList<DirectoryItemFilterSnapshot> items, DirectoryFilterSpec spec)
			=> EvaluateDetailed(items, spec).ItemIds;

		public static DirectoryFilterEvaluationResult EvaluateDetailed(
			IReadOnlyList<DirectoryItemFilterSnapshot> items,
			DirectoryFilterSpec spec,
			CancellationToken cancellationToken = default)
		{
			var results = new List<long>(items.Count);

			if (spec.IsEmpty)
			{
				for (var index = 0; index < items.Count; index++)
					results.Add(items[index].ItemId);

				return new(results, DirectoryRegexError.None);
			}

			DirectoryRegexFilter? regexFilter = null;
			if (spec.Regex is { } regexOptions
				&& !DirectoryRegexFilter.TryCreate(regexOptions, out regexFilter, out var createError))
			{
				return new([], createError);
			}

			if (!TryCreateConditionRegexes(spec, out var conditionRegexes, out var conditionError))
				return new([], conditionError);

			var stopwatch = regexFilter is not null || conditionRegexes is not null
				? Stopwatch.StartNew()
				: null;

			try
			{
				for (var index = 0; index < items.Count; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (stopwatch is not null && stopwatch.Elapsed > RegexTotalBudget)
						return new([], DirectoryRegexError.Timeout);

					if (regexFilter is not null && !regexFilter.IsMatch(items[index]))
						continue;

					if (Matches(items[index], spec, conditionRegexes))
						results.Add(items[index].ItemId);
				}
			}
			catch (RegexMatchTimeoutException)
			{
				// Never commit a partial result for a regex that ran away.
				return new([], DirectoryRegexError.Timeout);
			}

			return new(results, DirectoryRegexError.None);
		}

		public static bool Matches(in DirectoryItemFilterSnapshot item, DirectoryFilterSpec spec)
		{
			if (!TryCreateConditionRegexes(spec, out var conditionRegexes, out _))
				return false;

			return Matches(item, spec, conditionRegexes);
		}

		private static bool Matches(
			in DirectoryItemFilterSnapshot item,
			DirectoryFilterSpec spec,
			DirectoryRegexFilter?[]? conditionRegexes)
		{
			if (!string.IsNullOrEmpty(spec.NameContains)
				&& !item.DisplayName.Contains(spec.NameContains, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			if (spec.Columns.Count == 0)
				return true;

			// AND binds tighter than OR: each run of And-joined conditions forms one group,
			// and the completed groups are OR-ed together. The first condition starts the first
			// group, so its Join is not applicable.
			var total = false;
			var group = MatchesColumn(item, spec.Columns[0], conditionRegexes?[0]);

			for (var index = 1; index < spec.Columns.Count; index++)
			{
				var filter = spec.Columns[index];
				var matches = MatchesColumn(item, filter, conditionRegexes?[index]);

				if (filter.Join is DirectoryConditionJoin.Or)
				{
					total |= group;
					group = matches;
				}
				else
				{
					group &= matches;
				}
			}

			return total || group;
		}

		private static bool TryCreateConditionRegexes(
			DirectoryFilterSpec spec,
			out DirectoryRegexFilter?[]? conditionRegexes,
			out DirectoryRegexError error)
		{
			conditionRegexes = null;
			error = DirectoryRegexError.None;

			for (var index = 0; index < spec.Columns.Count; index++)
			{
				if (spec.Columns[index].Regex is not { } options)
					continue;

				conditionRegexes ??= new DirectoryRegexFilter?[spec.Columns.Count];

				if (!DirectoryRegexFilter.TryCreate(options, out var filter, out error))
					return false;

				conditionRegexes[index] = filter;
			}

			return true;
		}

		private static bool MatchesColumn(
			in DirectoryItemFilterSnapshot item,
			DirectoryColumnFilter filter,
			DirectoryRegexFilter? conditionRegex)
			=> filter.Column switch
			{
				DirectoryColumnKind.Name when conditionRegex is not null => conditionRegex.IsMatch(item),
				DirectoryColumnKind.Name => MatchesText(
					filter.Scope is DirectoryNameScope.RawName ? item.RawName : item.DisplayName,
					filter),
				DirectoryColumnKind.Type => MatchesChoices(item.ItemType, filter, normalizeExtension: false),
				DirectoryColumnKind.Extension => MatchesChoices(item.Extension, filter, normalizeExtension: true),
				DirectoryColumnKind.Tags => MatchesTags(item.TagIds, filter),
				DirectoryColumnKind.Size => MatchesSize(item.SizeBytes, filter),
				DirectoryColumnKind.ModifiedDate => MatchesDate(item.ModifiedUtc, filter),
				DirectoryColumnKind.CreatedDate => MatchesDate(item.CreatedUtc, filter),
				_ => true,
			};

		private static bool MatchesText(string? value, DirectoryColumnFilter filter)
		{
			value ??= string.Empty;

			if (IsLengthOperator(filter.Operator))
			{
				if (filter.Length is not { } length)
					return true;

				// Length counts UTF-16 code units, so astral characters count as two.
				return filter.Operator switch
				{
					DirectoryTextOperator.LengthGreaterThan => value.Length > length,
					DirectoryTextOperator.LengthLessThan => value.Length < length,
					DirectoryTextOperator.LengthEquals => value.Length == length,
					_ => value.Length != length,
				};
			}

			if (string.IsNullOrEmpty(filter.Text))
				return true;

			return filter.Operator switch
			{
				DirectoryTextOperator.NotContains => !value.Contains(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.StartsWith => value.StartsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.StartsWithNot => !value.StartsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.EndsWith => value.EndsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.EndsWithNot => !value.EndsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.Equals => string.Equals(value, filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.NotEquals => !string.Equals(value, filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.Wildcard => MatchesWildcard(value, filter.Text),
				_ => value.Contains(filter.Text, StringComparison.OrdinalIgnoreCase),
			};
		}

		private static bool IsLengthOperator(DirectoryTextOperator textOperator)
			=> textOperator is DirectoryTextOperator.LengthGreaterThan
				or DirectoryTextOperator.LengthLessThan
				or DirectoryTextOperator.LengthEquals
				or DirectoryTextOperator.LengthNotEquals;

		private static bool MatchesWildcard(string value, string pattern)
			=> WildcardRegexCache.GetOrAdd(pattern, CreateWildcardRegex).IsMatch(value);

		private static Regex CreateWildcardRegex(string pattern)
		{
			var builder = new StringBuilder(pattern.Length + 8);
			builder.Append('^');

			foreach (var character in pattern)
			{
				switch (character)
				{
					case '*':
						builder.Append(".*");
						break;

					case '?':
						builder.Append('.');
						break;

					default:
						builder.Append(Regex.Escape(character.ToString()));
						break;
				}
			}

			builder.Append('$');

			return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, DirectoryRegexFilter.MatchTimeout);
		}

		private static bool MatchesChoices(string? value, DirectoryColumnFilter filter, bool normalizeExtension)
		{
			var hasValue = !string.IsNullOrEmpty(value);

			if (filter.Values.Count == 0)
				return !filter.IncludeEmpty || !hasValue;

			if (hasValue)
			{
				var candidate = normalizeExtension ? NormalizeExtension(value) : value;

				for (var index = 0; index < filter.Values.Count; index++)
				{
					var selected = filter.Values[index];

					if (normalizeExtension)
						selected = NormalizeExtension(selected);

					if (string.Equals(candidate, selected, StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}

			return filter.IncludeEmpty && !hasValue;
		}

		private static bool MatchesTags(IReadOnlyList<string> tagIds, DirectoryColumnFilter filter)
		{
			var hasTags = tagIds is { Count: > 0 };

			if (filter.Values.Count == 0)
				return !filter.IncludeEmpty || !hasTags;

			if (hasTags)
			{
				for (var tagIndex = 0; tagIndex < tagIds.Count; tagIndex++)
				{
					for (var valueIndex = 0; valueIndex < filter.Values.Count; valueIndex++)
					{
						if (string.Equals(tagIds[tagIndex], filter.Values[valueIndex], StringComparison.OrdinalIgnoreCase))
							return true;
					}
				}
			}

			return filter.IncludeEmpty && !hasTags;
		}

		private static bool MatchesSize(long? sizeBytes, DirectoryColumnFilter filter)
		{
			if (filter.MinSize is null && filter.MaxSize is null)
				return true;

			// Unknown sizes (common for folders) are not zero and cannot satisfy a size condition.
			if (sizeBytes is not { } size)
				return false;

			if (filter.MinSize is { } min && size < min)
				return false;

			if (filter.MaxSize is { } max && size > max)
				return false;

			return true;
		}

		private static bool MatchesDate(DateTimeOffset? value, DirectoryColumnFilter filter)
		{
			if (filter.MinDate is null && filter.MaxDate is null)
				return true;

			if (value is not { } date)
				return false;

			if (filter.MinDate is { } min && date < min)
				return false;

			if (filter.MaxDate is { } max && date > max)
				return false;

			return true;
		}

		private static string NormalizeExtension(string? extension)
			=> extension?.TrimStart('.') ?? string.Empty;
	}
}
