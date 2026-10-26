// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Scalar copy of a listed item used to evaluate filters off the UI thread.
	/// Unknown size and dates stay null so conditions never treat them as 0 or the smallest value.
	/// </summary>
	public readonly record struct DirectoryItemFilterSnapshot(
		long ItemId,
		string RawName,
		string DisplayName,
		string FullPath,
		string? Extension,
		string? ItemType,
		IReadOnlyList<string> TagIds,
		long? SizeBytes,
		DateTimeOffset? ModifiedUtc,
		DateTimeOffset? CreatedUtc,
		bool IsFolder);
}
