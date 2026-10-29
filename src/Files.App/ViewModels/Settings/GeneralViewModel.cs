// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;
using static Files.App.Helpers.MenuFlyoutHelper;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Files.App.ViewModels.Settings
{
	public sealed partial class GeneralViewModel : ObservableObject, IDisposable
	{
		private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private ICommonDialogService CommonDialogService { get; } = Ioc.Default.GetRequiredService<ICommonDialogService>();

		private bool disposed;

		private ReadOnlyCollection<IMenuFlyoutItemViewModel>? addFlyoutItemsSource;

		public RelayCommand ChangePageCommand { get; }
		public RelayCommand<PageOnStartupViewModel> RemovePageCommand { get; }
		public RelayCommand<string> AddPageCommand { get; }
		public RelayCommand RestartCommand { get; }
		public RelayCommand CancelRestartCommand { get; }

		private bool showRestartControl;
		public bool ShowRestartControl
		{
			get => showRestartControl;
			set => SetProperty(ref showRestartControl, value);
		}

		private int selectedPageIndex = -1;
		public int SelectedPageIndex
		{
			get => selectedPageIndex;
			set
			{
				if (SetProperty(ref selectedPageIndex, value))
					IsPageListEditEnabled = value >= 0;
			}
		}

		private bool isPageListEditEnabled;
		public bool IsPageListEditEnabled
		{
			get => isPageListEditEnabled;
			set => SetProperty(ref isPageListEditEnabled, value);
		}

		public int SelectedDateTimeFormatIndex
		{
			get => DateTimeFormatSettings.SelectedGlobalFormatIndex;
			set
			{
				if (value < 0 || value >= DateTimeFormatSettings.FormatChoices.Count)
					return;

				if (value == DateTimeFormatSettings.SelectedGlobalFormatIndex)
					return;

				DateTimeFormatSettings.SelectedGlobalFormatIndex = value;
				var format = DateTimeFormatSettings.SelectedGlobalFormat;

				if (format is DateTimeFormats.Custom)
					UpdateCustomDateFormatItem();

				OnPropertyChanged();
				OnPropertyChanged(nameof(DateFormatSample));
				OnPropertyChanged(nameof(ShowCustomDateFormatPattern));
				OnPropertyChanged(nameof(HasGlobalDateFormatError));
				OnPropertyChanged(nameof(GlobalDateFormatErrorMessage));
			}
		}

		private int selectedAppLanguageIndex;
		public int SelectedAppLanguageIndex
		{
			get => selectedAppLanguageIndex;
			set
			{
				if (AppLanguageHelper.TryChange(value))
				{
					selectedAppLanguageIndex = value;
					OnPropertyChanged(nameof(SelectedAppLanguageIndex));
					ShowRestartControl = true;
				}
			}
		}

		public List<DateTimeFormatItem> DateFormats { get; set; } = null!;

		public DateTimeFormatSettingsViewModel DateTimeFormatSettings { get; }

		public string GlobalCustomPattern
		{
			get => DateTimeFormatSettings.GlobalCustomPattern;
			set
			{
				if (value == DateTimeFormatSettings.GlobalCustomPattern)
					return;

				DateTimeFormatSettings.GlobalCustomPattern = value ?? string.Empty;
				OnPropertyChanged();
				OnPropertyChanged(nameof(DateFormatSample));
				OnPropertyChanged(nameof(HasGlobalDateFormatError));
				OnPropertyChanged(nameof(GlobalDateFormatErrorMessage));
				UpdateCustomDateFormatItem();
			}
		}

		public bool ShowCustomDateFormatPattern
			=> DateTimeFormatSettings.ShowGlobalCustomPattern;

		public bool HasGlobalDateFormatError
			=> DateTimeFormatSettings.HasGlobalValidationError;

		public string GlobalDateFormatErrorMessage
			=> DateTimeFormatSettings.GlobalValidationErrorMessage;

		public ObservableCollection<AppLanguageItem> AppLanguages => AppLanguageHelper.SupportedLanguages;

		public Dictionary<ShellPaneArrangement, string> ShellPaneArrangementTypes { get; private set; } = [];

		public GeneralViewModel()
		{
			ChangePageCommand = new RelayCommand(ChangePageAsync);
			RemovePageCommand = new RelayCommand<PageOnStartupViewModel>(RemovePage);
			AddPageCommand = new RelayCommand<string>(async (path) => await AddPageAsync(path));
			RestartCommand = new RelayCommand(DoRestartAsync);
			CancelRestartCommand = new RelayCommand(DoCancelRestart);

			selectedAppLanguageIndex = AppLanguageHelper.SupportedLanguages.IndexOf(AppLanguageHelper.PreferredLanguage);

			DateTimeFormatSettings = new(
				UserSettingsService.GeneralSettingsService,
				Ioc.Default.GetRequiredService<IDateTimeFormatterFactory>(),
				Ioc.Default.GetRequiredService<IVolumePathProvider>());

			AddDateTimeOptions();

			dispatcherQueue = DispatcherQueue.GetForCurrentThread();

			if (UserSettingsService.GeneralSettingsService.TabsOnStartupList is not null)
				PagesOnStartupList = new ObservableCollection<PageOnStartupViewModel>(UserSettingsService.GeneralSettingsService.TabsOnStartupList.Select((p) => new PageOnStartupViewModel(p)));
			else
				PagesOnStartupList = [];

			PagesOnStartupList.CollectionChanged += PagesOnStartupList_CollectionChanged;

			// ShellPaneArrangement
			ShellPaneArrangementTypes.Add(ShellPaneArrangement.Vertical, Strings.Vertical.GetLocalizedResource());
			ShellPaneArrangementTypes.Add(ShellPaneArrangement.Horizontal, Strings.Horizontal.GetLocalizedResource());
			SelectedShellPaneArrangementType = ShellPaneArrangementTypes[UserSettingsService.GeneralSettingsService.ShellPaneArrangementOption];

			InitStartupSettingsRecentFoldersFlyout();
		}

		internal void StartListening()
		{
			UserSettingsService.OnSettingChangedEvent -= OnSharedSettingChanged;
			UserSettingsService.OnSettingChangedEvent += OnSharedSettingChanged;
			RefreshSettings();
		}

		internal void StopListening() => UserSettingsService.OnSettingChangedEvent -= OnSharedSettingChanged;

		private void OnSharedSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.NewValue is JsonElement)
				RefreshSettings(e.SettingName);
		}

		private void RefreshSettings(string? name = null)
		{
			var settings = UserSettingsService.GeneralSettingsService;
			if (name is null or nameof(IGeneralSettingsService.DateTimeFormat))
			{
				OnPropertyChanged(nameof(SelectedDateTimeFormatIndex));
				OnPropertyChanged(nameof(DateFormatSample));
			}
			if (name is null or nameof(IGeneralSettingsService.ShellPaneArrangementOption))
			{
				SetProperty(ref selectedShellPaneArrangementType, ShellPaneArrangementTypes[settings.ShellPaneArrangementOption], nameof(SelectedShellPaneArrangementType));
			}
			if (name is null or nameof(IGeneralSettingsService.ReverseTabScrollDirection))
			{
				OnPropertyChanged(nameof(SelectedTabScrollDirectionIndex));
			}
			if (name is null or nameof(IGeneralSettingsService.OpenNewTabOnStartup) or nameof(IGeneralSettingsService.OpenSpecificPageOnStartup) or nameof(IGeneralSettingsService.ContinueLastSessionOnStartUp))
			{
				OnPropertyChanged(nameof(OpenNewTabPageOnStartup));
				OnPropertyChanged(nameof(OpenASpecificPageOnStartup));
				OnPropertyChanged(nameof(SelectedStartupSettingIndex));
			}
			if (name is null or nameof(IGeneralSettingsService.TabsOnStartupList))
			{
				if (name is not null || !PagesOnStartupList.Select(page => page.Path).SequenceEqual(settings.TabsOnStartupList ?? []))
				{
					PagesOnStartupList.CollectionChanged -= PagesOnStartupList_CollectionChanged;
					PagesOnStartupList.Clear();
					foreach (var path in settings.TabsOnStartupList ?? [])
						PagesOnStartupList.Add(new PageOnStartupViewModel(path));
					PagesOnStartupList.CollectionChanged += PagesOnStartupList_CollectionChanged;
					SelectedPageIndex = -1;
				}
			}
			OnPropertyChanged(name ?? string.Empty);
		}

		private async void DoRestartAsync()
		{
			// Tells the app to restore tabs when it's next launched
			UserSettingsService.AppSettingsService.RestoreTabsOnStartup = true;

			// Save the updated tab list before restarting
			AppLifecycleHelper.SaveSessionTabs();

			// Launches a new instance of Files
			var relaunched = await AppProcessLauncher.RelaunchAsync();
			await CompleteRestartAsync(relaunched, Environment.Exit, ReportRestartFailureAsync);
		}

		internal static async Task CompleteRestartAsync(bool relaunched, Action<int> exit, Func<Task> reportFailure)
		{
			if (relaunched)
			{
				exit(0);
				return;
			}

			await reportFailure();
		}

		private static Task ReportRestartFailureAsync()
			=> DialogDisplayHelper.ShowDialogAsync(
				Strings.RestartFailedTitle.GetLocalizedResource(),
				Strings.RestartFailedDescription.GetLocalizedResource());

		private void DoCancelRestart()
		{
			ShowRestartControl = false;
		}

		private void AddDateTimeOptions()
		{
			DateTimeOffset sampleDate1 = DateTime.Now.AddSeconds(-5);
			DateTimeOffset sampleDate2 = new DateTime(sampleDate1.Year - 5, 12, 31, 14, 30, 0);

			DateFormats = DateTimeFormatSettings.FormatChoices
				.Select(choice => new DateTimeFormatItem(choice, sampleDate1, sampleDate2, DateTimeFormatSettings.GlobalCustomPattern))
				.ToList();
		}

		private void UpdateCustomDateFormatItem()
		{
			DateFormats?
				.FirstOrDefault(item => item.Choice.IsCustom)
				?.Update(DateTimeFormatSettings.GlobalCustomPattern);
		}

		private void InitStartupSettingsRecentFoldersFlyout()
		{
			var recentsItem = new MenuFlyoutSubItemViewModel(Strings.JumpListRecentGroupHeader.GetLocalizedResource());
			recentsItem.Items.Add(new MenuFlyoutItemViewModel(Strings.Home.GetLocalizedResource())
			{
				Command = AddPageCommand,
				CommandParameter = "Home",
				Tooltip = Strings.Home.GetLocalizedResource()
			});
			recentsItem.Items.Add(new MenuFlyoutItemViewModel(Strings.Browse.GetLocalizedResource()) { Command = AddPageCommand });
		}

		private void PagesOnStartupList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (PagesOnStartupList.Count > 0)
				UserSettingsService.GeneralSettingsService.TabsOnStartupList = PagesOnStartupList.Select((p) => p.Path).ToList();
			else
				UserSettingsService.GeneralSettingsService.TabsOnStartupList = null;
		}

		public int SelectedStartupSettingIndex => ContinueLastSessionOnStartUp ? 1 : OpenASpecificPageOnStartup ? 2 : 0;

		public bool OpenNewTabPageOnStartup
		{
			get => UserSettingsService.GeneralSettingsService.OpenNewTabOnStartup;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.OpenNewTabOnStartup)
				{
					UserSettingsService.GeneralSettingsService.OpenNewTabOnStartup = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ContinueLastSessionOnStartUp
		{
			get => UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp)
				{
					UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp = value;

					OnPropertyChanged();
				}
			}
		}

		public bool OpenASpecificPageOnStartup
		{
			get => UserSettingsService.GeneralSettingsService.OpenSpecificPageOnStartup;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.OpenSpecificPageOnStartup)
				{
					UserSettingsService.GeneralSettingsService.OpenSpecificPageOnStartup = value;

					OnPropertyChanged();
				}
			}
		}

		public ObservableCollection<PageOnStartupViewModel> PagesOnStartupList { get; set; }

		public ReadOnlyCollection<IMenuFlyoutItemViewModel>? AddFlyoutItemsSource
		{
			get => addFlyoutItemsSource;
			set => SetProperty(ref addFlyoutItemsSource, value);
		}

		public bool OpenTabInExistingInstance
		{
			get => UserSettingsService.GeneralSettingsService.OpenTabInExistingInstance;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.OpenTabInExistingInstance)
				{
					UserSettingsService.GeneralSettingsService.OpenTabInExistingInstance = value;

					// Needed in Program.cs
					AppRuntime.LocalState.SetBoolean("OpenTabInExistingInstance", value);

					OnPropertyChanged();
				}
			}
		}

		public bool ShowOpenInNewPane
		{
			get => UserSettingsService.GeneralSettingsService.ShowOpenInNewPane;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowOpenInNewPane)
				{
					UserSettingsService.GeneralSettingsService.ShowOpenInNewPane = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ShowCreateFolderWithSelection
		{
			get => UserSettingsService.GeneralSettingsService.ShowCreateFolderWithSelection;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowCreateFolderWithSelection)
				{
					UserSettingsService.GeneralSettingsService.ShowCreateFolderWithSelection = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ShowCopyPath
		{
			get => UserSettingsService.GeneralSettingsService.ShowCopyPath;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowCopyPath)
				{
					UserSettingsService.GeneralSettingsService.ShowCopyPath = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ShowCreateAlternateDataStream
		{
			get => UserSettingsService.GeneralSettingsService.ShowCreateAlternateDataStream;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowCreateAlternateDataStream)
				{
					UserSettingsService.GeneralSettingsService.ShowCreateAlternateDataStream = value;

					OnPropertyChanged();
				}
			}
		}

		public bool ShowCreateShortcut
		{
			get => UserSettingsService.GeneralSettingsService.ShowCreateShortcut;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowCreateShortcut)
				{
					UserSettingsService.GeneralSettingsService.ShowCreateShortcut = value;

					OnPropertyChanged();
				}
			}
		}

		public bool AlwaysOpenDualPaneInNewTab
		{
			get => UserSettingsService.GeneralSettingsService.AlwaysOpenDualPaneInNewTab;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.AlwaysOpenDualPaneInNewTab)
				{
					UserSettingsService.GeneralSettingsService.AlwaysOpenDualPaneInNewTab = value;

					OnPropertyChanged();
				}
			}
		}

		public bool AlwaysSwitchToNewlyOpenedTab
		{
			get => UserSettingsService.GeneralSettingsService.AlwaysSwitchToNewlyOpenedTab;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.AlwaysSwitchToNewlyOpenedTab)
				{
					UserSettingsService.GeneralSettingsService.AlwaysSwitchToNewlyOpenedTab = value;

					OnPropertyChanged();
				}
			}
		}

		public int SelectedTabScrollDirectionIndex
		{
			get => UserSettingsService.GeneralSettingsService.ReverseTabScrollDirection ? 1 : 0;
			set
			{
				var reverse = value == 1;
				if (reverse != UserSettingsService.GeneralSettingsService.ReverseTabScrollDirection)
				{
					UserSettingsService.GeneralSettingsService.ReverseTabScrollDirection = reverse;

					OnPropertyChanged();
				}
			}
		}

		private void ChangePageAsync()
		{
			var result = CommonDialogService.Open_FileOpenDialog(MainWindow.Instance.WindowHandle, true, [], Environment.SpecialFolder.Desktop, out var filePath);
			if (result && SelectedPageIndex >= 0)
				PagesOnStartupList[SelectedPageIndex] = new PageOnStartupViewModel(filePath);
		}

		private void RemovePage(PageOnStartupViewModel? page)
		{
			if (page is not null)
				PagesOnStartupList.Remove(page);
		}

		private async Task AddPageAsync(string? path = null)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				bool result = CommonDialogService.Open_FileOpenDialog(MainWindow.Instance.WindowHandle, true, [], Environment.SpecialFolder.Desktop, out var filePath);
				if (!result)
					return;

				path = filePath;
			}

			if (!string.IsNullOrEmpty(path) && PagesOnStartupList is not null)
				PagesOnStartupList.Add(new PageOnStartupViewModel(path));
		}

		public string DateFormatSample
		{
			get
			{
				var index = SelectedDateTimeFormatIndex;

				if (index < 0 || index >= DateFormats.Count)
					return string.Empty;

				var item = DateFormats[index];
				return string.Format(Strings.DateFormatSample.GetLocalizedResource(), item.Sample1, item.Sample2);
			}
		}

		private DispatcherQueue dispatcherQueue;

		public DateTimeFormats DateTimeFormat
		{
			get => UserSettingsService.GeneralSettingsService.DateTimeFormat;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.DateTimeFormat)
				{
					UserSettingsService.GeneralSettingsService.DateTimeFormat = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowQuickAccessWidget
		{
			get => UserSettingsService.GeneralSettingsService.ShowQuickAccessWidget;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowQuickAccessWidget)
					UserSettingsService.GeneralSettingsService.ShowQuickAccessWidget = value;
			}
		}

		public bool ShowDrivesWidget
		{
			get => UserSettingsService.GeneralSettingsService.ShowDrivesWidget;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowDrivesWidget)
					UserSettingsService.GeneralSettingsService.ShowDrivesWidget = value;
			}
		}

		public bool ShowNetworkLocationsWidget
		{
			get => UserSettingsService.GeneralSettingsService.ShowNetworkLocationsWidget;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowNetworkLocationsWidget)
					UserSettingsService.GeneralSettingsService.ShowNetworkLocationsWidget = value;
			}
		}

		public bool ShowFileTagsWidget
		{
			get => UserSettingsService.GeneralSettingsService.ShowFileTagsWidget;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowFileTagsWidget)
				{
					UserSettingsService.GeneralSettingsService.ShowFileTagsWidget = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowRecentFilesWidget
		{
			get => UserSettingsService.GeneralSettingsService.ShowRecentFilesWidget;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowRecentFilesWidget)
					UserSettingsService.GeneralSettingsService.ShowRecentFilesWidget = value;
			}
		}

		public bool MoveShellExtensionsToSubMenu
		{
			get => UserSettingsService.GeneralSettingsService.MoveShellExtensionsToSubMenu;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.MoveShellExtensionsToSubMenu)
				{
					UserSettingsService.GeneralSettingsService.MoveShellExtensionsToSubMenu = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowPinToSideBar
		{
			get => UserSettingsService.GeneralSettingsService.ShowPinToSideBar;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowPinToSideBar)
				{
					UserSettingsService.GeneralSettingsService.ShowPinToSideBar = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowPinToStart
		{
			get => UserSettingsService.GeneralSettingsService.ShowPinToStart;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowPinToStart)
				{
					UserSettingsService.GeneralSettingsService.ShowPinToStart = value;
					OnPropertyChanged();
				}
			}
		}
		
		public bool ShowEditTagsMenu
		{
			get => UserSettingsService.GeneralSettingsService.ShowEditTagsMenu;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowEditTagsMenu)
				{
					UserSettingsService.GeneralSettingsService.ShowEditTagsMenu = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowOpenInNewTab
		{
			get => UserSettingsService.GeneralSettingsService.ShowOpenInNewTab;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowOpenInNewTab)
				{
					UserSettingsService.GeneralSettingsService.ShowOpenInNewTab = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowCompressionOptions
		{
			get => UserSettingsService.GeneralSettingsService.ShowCompressionOptions;
			set
			{
				if (value == UserSettingsService.GeneralSettingsService.ShowCompressionOptions)
					return;

				UserSettingsService.GeneralSettingsService.ShowCompressionOptions = value;
				OnPropertyChanged();
			}
		}

		// TODO uncomment code when feature is marked as stable
		//public bool ShowFlattenOptions
		//{
		//	get => UserSettingsService.GeneralSettingsService.ShowFlattenOptions;
		//	set
		//	{
		//		if (value == UserSettingsService.GeneralSettingsService.ShowFlattenOptions)
		//			return;

		//		UserSettingsService.GeneralSettingsService.ShowFlattenOptions = value;
		//		OnPropertyChanged();
		//	}
		//}

		public bool ShowSendToMenu
		{
			get => UserSettingsService.GeneralSettingsService.ShowSendToMenu;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowSendToMenu)
				{
					UserSettingsService.GeneralSettingsService.ShowSendToMenu = value;
					OnPropertyChanged();
				}
			}
		}

		public bool SupportsNewWindows
			=> AppProcessLauncher.SupportsNewWindows(AppRuntime.Current.Distribution);

		public bool ShowOpenInNewWindow
		{
			get => SupportsNewWindows && UserSettingsService.GeneralSettingsService.ShowOpenInNewWindow;
			set
			{
				if (SupportsNewWindows && value != UserSettingsService.GeneralSettingsService.ShowOpenInNewWindow)
				{
					UserSettingsService.GeneralSettingsService.ShowOpenInNewWindow = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowOpenTerminal
		{
			get => UserSettingsService.GeneralSettingsService.ShowOpenTerminal;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.ShowOpenTerminal)
				{
					UserSettingsService.GeneralSettingsService.ShowOpenTerminal = value;
					OnPropertyChanged();
				}
			}
		}

		private string selectedShellPaneArrangementType = null!;
		public string SelectedShellPaneArrangementType
		{
			get => selectedShellPaneArrangementType;
			set
			{
				if (SetProperty(ref selectedShellPaneArrangementType, value))
				{
					UserSettingsService.GeneralSettingsService.ShellPaneArrangementOption = ShellPaneArrangementTypes.First(e => e.Value == value).Key;
				}
			}
		}

		public bool EnableSmoothScrolling
		{
			get => UserSettingsService.GeneralSettingsService.EnableSmoothScrolling;
			set
			{
				if (value != UserSettingsService.GeneralSettingsService.EnableSmoothScrolling)
				{
					UserSettingsService.GeneralSettingsService.EnableSmoothScrolling = value;

					OnPropertyChanged();
				}
			}
		}

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				UserSettingsService.OnSettingChangedEvent -= OnSharedSettingChanged;

				GC.SuppressFinalize(this);
			}
		}

		~GeneralViewModel()
		{
			Dispose();
		}
	}

	public sealed class PageOnStartupViewModel
	{
		public string Text
		{
			get => ShellHelpers.GetShellNameFromPath(Path);
		}

		public string Path { get; }

		internal PageOnStartupViewModel(string path)
			=> Path = path;
	}

	public sealed class DateTimeFormatItem : INotifyPropertyChanged
	{
		private readonly DateTimeOffset _sampleDate1;
		private readonly DateTimeOffset _sampleDate2;

		private string sample1 = string.Empty;
		private string sample2 = string.Empty;

		public event PropertyChangedEventHandler? PropertyChanged;

		public DateTimeFormatChoice Choice { get; }

		public DateTimeFormats Format
			=> Choice.Format ?? DateTimeFormats.Application;

		public string Label
			=> Choice.Label;

		public override string ToString() => Label;

		public string Sample1
		{
			get => sample1;
			private set => SetProperty(ref sample1, value);
		}

		public string Sample2
		{
			get => sample2;
			private set => SetProperty(ref sample2, value);
		}

		public DateTimeFormatItem(DateTimeFormatChoice choice, DateTimeOffset sampleDate1, DateTimeOffset sampleDate2, string? customPattern = null)
		{
			Choice = choice;
			_sampleDate1 = sampleDate1;
			_sampleDate2 = sampleDate2;

			Update(customPattern);
		}

		public void Update(string? customPattern)
		{
			var factory = Ioc.Default.GetRequiredService<IDateTimeFormatterFactory>();
			var pattern = Choice.FixedPattern ?? (Choice.IsCustom ? customPattern : null);
			var formatter = factory.GetDateTimeFormatter(Format, pattern);

			Sample1 = formatter.ToShortLabel(_sampleDate1);
			Sample2 = formatter.ToShortLabel(_sampleDate2);
			OnPropertyChanged(nameof(Label));
		}

		private void OnPropertyChanged(string propertyName)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private void SetProperty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(storage, value))
				return;

			storage = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
