// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel;

namespace Files.App.Services
{
	internal sealed class PortableSystemIntegrationService : ISystemIntegrationService
	{
		public Task<StartupTaskState> GetStartupTaskStateAsync() => Task.FromResult(StartupTaskState.Disabled);
		public Task SetStartupTaskEnabledAsync(bool enabled) => Task.CompletedTask;
		public bool IsDefaultFileManager() => false;
		public Task SetDefaultFileManagerAsync(bool enabled) => Task.CompletedTask;
		public bool IsOpenFileDialog() => false;
		public Task SetOpenFileDialogAsync(bool enabled) => Task.CompletedTask;
	}
}
