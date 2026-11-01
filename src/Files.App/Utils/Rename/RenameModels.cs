// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// Selects which timestamp the rename date variables read from.
	/// </summary>
	public enum FileDateSource
	{
		Created,
		Modified,
	}

	/// <summary>
	/// How the Find term of a rename rule is interpreted.
	/// </summary>
	public enum RenameMode
	{
		Text,
		Wildcard,
		Regex,
	}

	/// <summary>
	/// Which part of a file name a rename rule is applied to.
	/// </summary>
	public enum RenameApplyTo
	{
		NameOnly,
		ExtensionOnly,
		NameAndExtension,
	}

	/// <summary>
	/// The shape of change a rename rule describes.
	/// </summary>
	public enum RenameTemplate
	{
		FindReplace,
		Prepend,
		Append,
	}

	/// <summary>
	/// Optional casing transform applied to the name part after the rule.
	/// </summary>
	public enum RenameCaseConversion
	{
		None,
		Lowercase,
		Uppercase,
		FirstLetter,
		EveryWord,
	}

	/// <summary>
	/// Which items a batch rename operates on.
	/// </summary>
	public enum RenameScopeOption
	{
		SelectedItems,
		CurrentFolder,
		CurrentFolderWithSubfolders,
	}

	/// <summary>
	/// What to do when a proposed name collides with another name.
	/// </summary>
	public enum ConflictStrategy
	{
		AppendNumber,
		Skip,
		Overwrite,
	}
}
