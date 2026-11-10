// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils
{
	public sealed class QuickAccessManager
	{


		public EventHandler<ModifyQuickAccessEventArgs>? UpdateQuickAccessWidget;

		public IQuickAccessService QuickAccessService;

		public PinnedFoldersManager Model;
		public QuickAccessManager()
		{
			QuickAccessService = Ioc.Default.GetRequiredService<IQuickAccessService>();
			Model = new();
			Initialize();
		}

		public void Initialize() => App.ServerChanged += OnServerChanged;

		private async void OnServerChanged(object? sender, string name)
		{
			if (name == ServerChangeNames.PinnedFolders)
				await SafetyExtensions.IgnoreExceptions(Model.ReloadAsync, App.Logger);
		}

		public async Task InitializeAsync()
		{
			await Model.LoadAsync();

			if (ShouldPinRecycleBinOnFirstRun(
				AppRuntime.Current.SupportsSystemIntegration,
				AppLifecycleHelper.IsFirstRun,
				Model.PinnedFolders.Contains(Constants.UserEnvironmentPaths.RecycleBinPath)))
			{
				await QuickAccessService.PinToSidebarAsync(Constants.UserEnvironmentPaths.RecycleBinPath);
			}
		}

		internal static bool ShouldPinRecycleBinOnFirstRun(bool supportsSystemIntegration, bool isFirstRun, bool isAlreadyPinned)
			=> supportsSystemIntegration && isFirstRun && !isAlreadyPinned;
	}
}
