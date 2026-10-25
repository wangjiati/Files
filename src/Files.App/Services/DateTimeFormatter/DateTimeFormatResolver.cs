// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.DateTimeFormatter
{
	internal sealed class DateTimeFormatResolver : IDateTimeFormatResolver
	{
		private readonly IDateTimeFormatRuleSource _ruleSource;
		private readonly IVolumePathProvider _volumePathProvider;

		public DateTimeFormatResolver(IDateTimeFormatRuleSource ruleSource, IVolumePathProvider volumePathProvider)
		{
			_ruleSource = ruleSource;
			_volumePathProvider = volumePathProvider;
		}

		public DateTimeFormatRule Resolve(string directoryPath, DateTimeColumnKind column)
		{
			if (!string.IsNullOrWhiteSpace(directoryPath))
			{
				var directoryKey = PortableDirectoryKey.FromPath(directoryPath, _volumePathProvider);
				var directoryRule = FindDirectoryRule(directoryKey, column);

				if (directoryRule is not null)
					return directoryRule;
			}

			var globalColumnRule = FindGlobalColumnRule(column);

			if (globalColumnRule is not null)
				return globalColumnRule;

			return new DateTimeFormatRule(
				column,
				null,
				false,
				_ruleSource.DefaultDateTimeFormat,
				_ruleSource.DefaultDateTimeFormatPattern);
		}

		private DateTimeFormatRule? FindDirectoryRule(PortableDirectoryKey directoryKey, DateTimeColumnKind column)
		{
			DateTimeFormatRule? bestColumnRule = null;
			DateTimeFormatRule? bestDefaultRule = null;
			var bestColumnDistance = int.MaxValue;
			var bestDefaultDistance = int.MaxValue;

			foreach (var candidate in _ruleSource.DirectoryFormatRules)
			{
				if (candidate is null ||
					candidate.Validate() is not DateTimeFormatRuleValidationError.None ||
					string.IsNullOrWhiteSpace(candidate.DirectoryPath))
				{
					continue;
				}

				if (candidate.Column is { } candidateColumn && candidateColumn != column)
					continue;

				var scopeKey = PortableDirectoryKey.FromRule(candidate);

				if (!scopeKey.TryGetDistance(directoryKey, candidate.IncludeSubdirectories, out var distance))
					continue;

				if (candidate.Column is not null)
				{
					// A nearer rule wins; equal distances keep the last rule for the same scope and column.
					if (distance <= bestColumnDistance)
					{
						bestColumnRule = candidate;
						bestColumnDistance = distance;
					}
				}
				else if (distance <= bestDefaultDistance)
				{
					bestDefaultRule = candidate;
					bestDefaultDistance = distance;
				}
			}

			return bestColumnRule ?? bestDefaultRule;
		}

		private DateTimeFormatRule? FindGlobalColumnRule(DateTimeColumnKind column)
		{
			DateTimeFormatRule? rule = null;

			foreach (var candidate in _ruleSource.ColumnFormatRules)
			{
				if (candidate is null ||
					candidate.Validate() is not DateTimeFormatRuleValidationError.None ||
					candidate.Column != column ||
					!string.IsNullOrWhiteSpace(candidate.DirectoryPath))
				{
					continue;
				}

				rule = candidate;
			}

			return rule;
		}
	}
}
