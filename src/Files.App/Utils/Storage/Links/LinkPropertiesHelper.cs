// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Data.Models;
using Files.App.Helpers;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace Files.App.Utils.Storage.Links
{
	public static class LinkPropertiesHelper
	{
		public static void Populate(SelectedItemsPropertiesViewModel viewModel, ListedItem item)
		{
			if (!item.IsFileSystemLink)
				return;

			viewModel.IsFileSystemLink = true;

			var info = Win32Helper.GetReparsePointInfo(item.ItemPath ?? string.Empty);
			var targetPath = item.LinkTargetPath ?? info?.TargetPath;

			viewModel.LinkType = LinkTypeHelper.GetLabel(item.LinkKind, item.IsFolder);
			viewModel.LinkTargetPath = targetPath;

			if (item.LinkKind == LinkKind.SymbolicLink)
			{
				viewModel.LinkPathType = info?.IsRelative == true
					? Strings.LinkPathTypeRelative.GetLocalizedResource()
					: Strings.LinkPathTypeAbsolute.GetLocalizedResource();
				viewModel.IsLinkPathTypeVisible = true;
			}

			var isHardLink = item.LinkKind == LinkKind.HardLink;
			viewModel.IsHardLink = isHardLink;
			viewModel.LinkHardLinkCount = item.HardLinkCount;
			viewModel.LinkHardLinkCountText = item.HardLinkCount?.ToString();
			viewModel.IsLinkTargetVisible = !isHardLink;

			viewModel.LinkTargetStatus = GetTargetStatus(targetPath, item.LinkKind, out var missing);
			viewModel.IsLinkTargetMissing = missing;

			viewModel.OpenLinkTargetCommand = new RelayCommand(async () =>
			{
				if (string.IsNullOrEmpty(targetPath) || missing)
					return;

				var pathToOpen = Directory.Exists(targetPath)
					? targetPath
					: Path.GetDirectoryName(targetPath);

				if (!string.IsNullOrEmpty(pathToOpen))
					await NavigationHelpers.OpenPathInNewTab(pathToOpen, true);
			});

			viewModel.CopyLinkTargetCommand = new RelayCommand(() =>
			{
				if (string.IsNullOrEmpty(targetPath))
					return;

				var dataPackage = new DataPackage();
				dataPackage.SetText(targetPath);
				Clipboard.SetContent(dataPackage);
			});
		}

		private static string GetTargetStatus(string? targetPath, LinkKind linkKind, out bool missing)
		{
			if (string.IsNullOrEmpty(targetPath))
			{
				missing = true;
				return Strings.LinkTargetMissing.GetLocalizedResource();
			}

			try
			{
				if (Directory.Exists(targetPath) || File.Exists(targetPath) || linkKind == LinkKind.VolumeMountPoint)
				{
					missing = false;
					return Strings.LinkTargetExists.GetLocalizedResource();
				}

				missing = true;
				return Strings.LinkTargetMissing.GetLocalizedResource();
			}
			catch
			{
				missing = true;
				return Strings.LinkTargetNoAccess.GetLocalizedResource();
			}
		}
	}
}
