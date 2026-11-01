// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// A named, saved batch rename configuration that can be restored later.
	/// </summary>
	public sealed record RenamePreset(
		string Name,
		RenameRule Rule,
		RenameScopeOption Scope,
		int Depth,
		bool IncludeFiles,
		bool IncludeFolders);
}
