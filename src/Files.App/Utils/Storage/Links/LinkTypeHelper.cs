// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Utils.Storage.Links
{
	public static class LinkTypeHelper
	{
		public static string Compose(string? baseType, string? linkLabel)
		{
			if (string.IsNullOrEmpty(baseType))
				return linkLabel ?? string.Empty;

			if (string.IsNullOrEmpty(linkLabel))
				return baseType;

			return $"{baseType} · {linkLabel}";
		}

		public static string GetLabel(LinkKind kind, bool isDirectory)
		{
			return kind switch
			{
				LinkKind.ShellShortcut => Strings.Shortcut.GetLocalizedResource(),
				LinkKind.InternetShortcut => Strings.ShortcutWebLinkFileType.GetLocalizedResource(),
				LinkKind.SymbolicLink => (isDirectory ? Strings.LinkTypeSymbolicFolder : Strings.LinkTypeSymbolicFile).GetLocalizedResource(),
				LinkKind.Junction => Strings.LinkTypeJunction.GetLocalizedResource(),
				LinkKind.HardLink => Strings.LinkTypeHardLink.GetLocalizedResource(),
				LinkKind.VolumeMountPoint => Strings.LinkTypeVolumeMountPoint.GetLocalizedResource(),
				_ => string.Empty
			};
		}
	}
}
