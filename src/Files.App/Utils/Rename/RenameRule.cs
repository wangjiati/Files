// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// A complete batch rename rule. Defaults are the plan's pinned defaults.
	/// </summary>
	public sealed record RenameRule(
		RenameTemplate Template = RenameTemplate.FindReplace,
		string Find = "",
		string ReplaceWith = "",
		RenameMode Mode = RenameMode.Text,
		RenameApplyTo ApplyTo = RenameApplyTo.NameOnly,
		bool MatchAllOccurrences = true,
		bool CaseSensitive = false,
		string Prefix = "",
		bool SkipItemsWithPrefix = true,
		string Suffix = "",
		bool InsertBeforeExtension = true,
		RenameCaseConversion CaseConversion = RenameCaseConversion.None,
		FileDateSource DateSource = FileDateSource.Created);
}
