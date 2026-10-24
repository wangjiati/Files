// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel;

namespace Files.App.Data.Contracts
{
	internal interface ISystemIntegrationService
	{
		Task<StartupTaskState> GetStartupTaskStateAsync();
		Task SetStartupTaskEnabledAsync(bool enabled);
		bool IsDefaultFileManager();
		Task SetDefaultFileManagerAsync(bool enabled);
		bool IsOpenFileDialog();
		Task SetOpenFileDialogAsync(bool enabled);
	}
}
