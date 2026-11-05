// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using FileLinkKind = Files.App.Data.Enums.LinkKind;

namespace Files.App.Utils
{
	public sealed partial class LinkItem : ListedItem
	{
		public LinkItem(FileLinkKind linkKind)
		{
			LinkKind = linkKind;
		}

		public string? TargetPath => LinkTargetPath;

		public override bool IsExecutable
			=> FileExtensionHelpers.IsExecutableFile(LinkTargetPath ?? ItemPath, true);

		public override bool IsScriptFile
			=> FileExtensionHelpers.IsScriptFile(LinkTargetPath);
	}

	public sealed partial class GitLinkItem : GitItem
	{
		public GitLinkItem(FileLinkKind linkKind)
		{
			LinkKind = linkKind;
		}

		public string? TargetPath => LinkTargetPath;

		public override bool IsExecutable
			=> FileExtensionHelpers.IsExecutableFile(LinkTargetPath ?? ItemPath, true);

		public override bool IsScriptFile
			=> FileExtensionHelpers.IsScriptFile(LinkTargetPath);
	}
}
