// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Identifies the column a directory filter condition applies to.
	/// </summary>
	public enum DirectoryColumnKind
	{
		Name,
		Type,
		Extension,
		Tags,
		Size,
		ModifiedDate,
		CreatedDate,
	}

	/// <summary>
	/// Text comparison used by name column filters.
	/// </summary>
	public enum DirectoryTextOperator
	{
		Contains,
		NotContains,
		StartsWith,
		EndsWith,
		Equals,
	}

	/// <summary>
	/// A single immutable column condition. Conditions on different columns are combined with AND,
	/// while multiple selected values inside one condition are combined with OR.
	/// </summary>
	public sealed record DirectoryColumnFilter
	{
		public DirectoryColumnKind Column { get; init; }

		public DirectoryTextOperator Operator { get; init; } = DirectoryTextOperator.Contains;

		/// <summary>
		/// Text used by <see cref="DirectoryColumnKind.Name"/> conditions.
		/// </summary>
		public string? Text { get; init; }

		/// <summary>
		/// Selected values used by type, extension and tag conditions.
		/// </summary>
		public IReadOnlyList<string> Values { get; init; } = [];

		public long? MinSize { get; init; }

		public long? MaxSize { get; init; }

		public DateTimeOffset? MinDate { get; init; }

		public DateTimeOffset? MaxDate { get; init; }

		/// <summary>
		/// Matches items whose value is missing or empty, such as untagged items or files without an extension.
		/// </summary>
		public bool IncludeEmpty { get; init; }
	}

	/// <summary>
	/// The complete immutable set of temporary filters applied to the loaded directory items.
	/// </summary>
	public sealed record DirectoryFilterSpec
	{
		public static DirectoryFilterSpec Empty { get; } = new();

		/// <summary>
		/// Legacy name box filter. Matched against the current display name.
		/// </summary>
		public string? NameContains { get; init; }

		public IReadOnlyList<DirectoryColumnFilter> Columns { get; init; } = [];

		public bool IsEmpty
			=> string.IsNullOrEmpty(NameContains) && Columns.Count == 0;
	}
}
