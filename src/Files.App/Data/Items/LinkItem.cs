// Copyright (c) Files Community
// Licensed under the MIT License.

using FileLinkKind = Files.App.Data.Enums.LinkKind;

namespace Files.App.Utils
{
	public sealed partial class LinkItem : ListedItem
	{
		public LinkItem(FileLinkKind linkKind)
		{
			LinkKind = linkKind;
		}
	}

	public sealed partial class GitLinkItem : GitItem
	{
		public GitLinkItem(FileLinkKind linkKind)
		{
			LinkKind = linkKind;
		}
	}
}
