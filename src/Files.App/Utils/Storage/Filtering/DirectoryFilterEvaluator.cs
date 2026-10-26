// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

			var stopwatch = regexFilter is null ? null : Stopwatch.StartNew();

			try
			{
				for (var index = 0; index < items.Count; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (regexFilter is not null)
					{
						if (stopwatch!.Elapsed > RegexTotalBudget)
							return new([], DirectoryRegexError.Timeout);

						if (!regexFilter.IsMatch(items[index]))
							continue;
					}

					if (Matches(items[index], spec))
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
			if (!string.IsNullOrEmpty(spec.NameContains)
				&& !item.DisplayName.Contains(spec.NameContains, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			for (var index = 0; index < spec.Columns.Count; index++)
			{
				if (!MatchesColumn(item, spec.Columns[index]))
					return false;
			}

			return true;
		}

		private static bool MatchesColumn(in DirectoryItemFilterSnapshot item, DirectoryColumnFilter filter)
			=> filter.Column switch
			{
				DirectoryColumnKind.Name => MatchesText(item.DisplayName, filter),
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
			if (string.IsNullOrEmpty(filter.Text))
				return true;

			value ??= string.Empty;

			return filter.Operator switch
			{
				DirectoryTextOperator.NotContains => !value.Contains(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.StartsWith => value.StartsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.EndsWith => value.EndsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
				DirectoryTextOperator.Equals => string.Equals(value, filter.Text, StringComparison.OrdinalIgnoreCase),
				_ => value.Contains(filter.Text, StringComparison.OrdinalIgnoreCase),
			};
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
