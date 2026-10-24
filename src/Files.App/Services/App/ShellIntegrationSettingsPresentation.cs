// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services
{
	internal enum ShellIntegrationStatusResource
	{
		DefaultFileManagerInternal,
		DefaultFileManagerWindows,
		DefaultFileManagerOther,
		DefaultFileManagerNone,
		OpenFileDialogInternal,
		OpenFileDialogOther,
		OpenFileDialogNone,
	}

	internal static class ShellIntegrationSettingsPresentation
	{
		public static bool ShowDefaultFileManagerSettings(bool supportsSystemIntegration)
			=> supportsSystemIntegration;

		public static bool ShowOpenFileDialogSettings(bool supportsSystemIntegration)
			=> supportsSystemIntegration;

		public static ShellIntegrationStatusResource GetDefaultFileManagerStatus(ShellIntegrationOwner owner)
			=> owner switch
			{
				ShellIntegrationOwner.Internal => ShellIntegrationStatusResource.DefaultFileManagerInternal,
				ShellIntegrationOwner.Windows => ShellIntegrationStatusResource.DefaultFileManagerWindows,
				ShellIntegrationOwner.Other => ShellIntegrationStatusResource.DefaultFileManagerOther,
				_ => ShellIntegrationStatusResource.DefaultFileManagerNone,
			};

		public static ShellIntegrationStatusResource GetOpenFileDialogStatus(ShellIntegrationOwner owner)
			=> owner switch
			{
				ShellIntegrationOwner.Internal => ShellIntegrationStatusResource.OpenFileDialogInternal,
				ShellIntegrationOwner.Other => ShellIntegrationStatusResource.OpenFileDialogOther,
				_ => ShellIntegrationStatusResource.OpenFileDialogNone,
			};
	}
}
