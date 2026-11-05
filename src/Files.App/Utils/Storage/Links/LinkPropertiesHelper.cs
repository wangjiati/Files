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

			var info = item.LinkKind == LinkKind.HardLink
				? null
				: Win32Helper.GetReparsePointInfo(item.ItemPath ?? string.Empty);
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

			viewModel.LinkTargetStatus = GetTargetStatus(targetPath, out var missing);
			viewModel.IsLinkTargetMissing = missing;

			viewModel.OpenLinkTargetCommand = new RelayCommand(async () =>
			{
				if (string.IsNullOrEmpty(targetPath) || missing)
					return;

				var isDirectory = Win32Helper.GetFileAttributes(targetPath)
					.HasFlag(System.IO.FileAttributes.Directory);
				var pathToOpen = isDirectory
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

		private static string GetTargetStatus(string? targetPath, out bool missing)
		{
			if (string.IsNullOrEmpty(targetPath))
			{
				missing = true;
				return Strings.LinkTargetMissing.GetLocalizedResource();
			}

			if (DriveHelpers.IsNetworkPath(targetPath))
			{
				missing = false;
				return Strings.LinkTargetNotChecked.GetLocalizedResource();
			}

			try
			{
				System.IO.File.GetAttributes(targetPath);
				missing = false;
				return Strings.LinkTargetExists.GetLocalizedResource();
			}
			catch (UnauthorizedAccessException)
			{
				missing = true;
				return Strings.LinkTargetNoAccess.GetLocalizedResource();
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or ArgumentException)
			{
				missing = true;
				return Strings.LinkTargetMissing.GetLocalizedResource();
			}
		}
	}
}
