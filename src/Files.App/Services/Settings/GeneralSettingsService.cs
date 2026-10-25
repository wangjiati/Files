// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Settings
{
	internal sealed partial class GeneralSettingsService : BaseObservableJsonSettings, IGeneralSettingsService, IDateTimeFormatRuleSource
	{
		private readonly IVolumePathProvider? _volumePathProvider;

		public GeneralSettingsService(ISettingsSharingContext settingsSharingContext, IVolumePathProvider? volumePathProvider = null)
		{
			_volumePathProvider = volumePathProvider;

			// Register root
			RegisterSettingsContext(settingsSharingContext);
		}

		public bool OpenSpecificPageOnStartup
		{
			get => Get(false);
			set => Set(value);
		}

		public string OpenSpecificPageOnStartupPath
		{
			get => Get(string.Empty);
			set => Set(value);
		}

		public bool ContinueLastSessionOnStartUp
		{
			get => Get(true);
			set => Set(value);
		}

		public bool OpenNewTabOnStartup
		{
			get => Get(false);
			set => Set(value);
		}

		public bool OpenTabInExistingInstance
		{
			get => Get(true);
			set => Set(value);
		}

		public List<string>? TabsOnStartupList
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public List<string>? LastSessionTabList
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public int LastSessionSelectedTabIndex
		{
			get => Get(-1);
			set => Set(value);
		}

		public List<string>? LastCrashedTabList
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public List<string>? PathHistoryList
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public List<string>? PreviousSearchQueriesList
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public List<string>? PreviousArchiveExtractionLocations
		{
			get => Get<List<string>>(null);
			set => Set(value);
		}

		public DateTimeFormats DateTimeFormat
		{
			get => Get(DateTimeFormats.Application);
			set => Set(value);
		}

		public string? DateTimeFormatCustomPattern
		{
			get => Get<string?>(null);
			set => Set(value);
		}

		public List<DateTimeFormatRule> DateTimeColumnFormatRules
		{
			get => Get<List<DateTimeFormatRule>>(null) ?? [];
			set => Set(NormalizeColumnRules(value));
		}

		public List<DateTimeFormatRule> DateTimeFormatDirectoryRules
		{
			get => Get<List<DateTimeFormatRule>>(null) ?? [];
			set => Set(NormalizeDirectoryRules(value));
		}

		public bool AlwaysOpenDualPaneInNewTab
		{
			get => Get(false);
			set => Set(value);
		}

		public bool AlwaysSwitchToNewlyOpenedTab
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ReverseTabScrollDirection
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ShowQuickAccessWidget
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowRecentFilesWidget
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowDrivesWidget
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowNetworkLocationsWidget
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowFileTagsWidget
		{
			get => Get(false);
			set => Set(value);
		}

		public bool FoldersWidgetExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool RecentFilesWidgetExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool DrivesWidgetExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool NetworkLocationsWidgetExpanded
		{
			get => Get(false);
			set => Set(value);
		}

		public bool FileTagsWidgetExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowPinnedSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowLibrarySection
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ShowDrivesSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowCloudDrivesSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowNetworkSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowWslSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowFileTagsSection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool IsPinnedSectionExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool IsLibrarySectionExpanded
		{
			get => Get(false);
			set => Set(value);
		}

		public bool IsDriveSectionExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool IsCloudDriveSectionExpanded
		{
			get => Get(true);
			set => Set(value);
		}

		public bool IsNetworkSectionExpanded
		{
			get => Get(false);
			set => Set(value);
		}

		public bool IsWslSectionExpanded
		{
			get => Get(false);
			set => Set(value);
		}

		public bool IsFileTagsSectionExpanded
		{
			get => Get(false);
			set => Set(value);
		}

		public bool MoveShellExtensionsToSubMenu
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowPinToSideBar
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowPinToStart
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowEditTagsMenu
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowCompressionOptions
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowFlattenOptions
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ShowSendToMenu
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowOpenInNewTab
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowOpenInNewWindow
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowOpenInNewPane
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowOpenTerminal
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowCopyPath
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowCreateFolderWithSelection
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowCreateAlternateDataStream
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ShowCreateShortcut
		{
			get => Get(true);
			set => Set(value);
		}

		public bool CloseToBackground
		{
			get => AppRuntime.Current.SupportsSystemIntegration &&
				Get(BackgroundSettings.CloseToBackgroundDefault, BackgroundSettings.CloseToBackgroundKey);
			set
			{
				// Portable has no tray or integration owner, so it must never keep running after the window closes.
				if (AppRuntime.Current.SupportsSystemIntegration)
					Set(value, BackgroundSettings.CloseToBackgroundKey);
			}
		}

		public bool EnableNaturalLanguageSearch
		{
			get => Get(true);
			set => Set(value);
		}

		public bool ShowSystemTrayIcon
		{
			get => Get(BackgroundSettings.ShowSystemTrayIconDefault, BackgroundSettings.ShowSystemTrayIconKey);
			set => Set(value, BackgroundSettings.ShowSystemTrayIconKey);
		}

		public FileNameConflictResolveOptionType ConflictsResolveOption
		{
			get => (FileNameConflictResolveOptionType)Get((long)FileNameConflictResolveOptionType.GenerateNewName);
			set => Set((long)value);
		}

		public ArchiveFormats ArchiveFormatsOption
		{
			get => (ArchiveFormats)Get((long)ArchiveFormats.Zip);
			set => Set((long)value);
		}

		public ArchiveCompressionLevels ArchiveCompressionLevelsOption
		{
			get => (ArchiveCompressionLevels)Get((long)ArchiveCompressionLevels.Normal);
			set => Set((long)value);
		}

		public ArchiveSplittingSizes ArchiveSplittingSizesOption
		{
			get => (ArchiveSplittingSizes)Get((long)ArchiveSplittingSizes.None);
			set => Set((long)value);
		}

		public ArchiveDictionarySizes ArchiveDictionarySizesOption
		{
			get => (ArchiveDictionarySizes)Get((long)ArchiveDictionarySizes.Auto);
			set => Set((long)value);
		}

		public ArchiveWordSizes ArchiveWordSizesOption
		{
			get => (ArchiveWordSizes)Get((long)ArchiveWordSizes.Auto);
			set => Set((long)value);
		}

		public Dictionary<string, bool>? ShowHashesDictionary
		{
			get => Get<Dictionary<string, bool>>(null);
			set => Set(value);
		}

		public string UserId
		{
			get
			{
				var userId = Get<string>(null);
				if (userId is null)
				{
					userId = Guid.NewGuid().ToString();
					Set(userId);
				}

				return userId;
			}
			set => Set(value);
		}

		public ShellPaneArrangement ShellPaneArrangementOption
		{
			get => (ShellPaneArrangement)Get((long)ShellPaneArrangement.Vertical);
			set => Set((long)value);
		}

		public bool ShowShelfPane
		{
			get => Get(false);
			set => Set(value);
		}

		public bool ShowFilterHeader
		{
			get => Get(false);
			set => Set(value);
		}

		public bool EnableThumbnailCache
		{
			get => Get(true);
			set => Set(value);
		}

		public bool EnableSmoothScrolling
		{
			get => Get(true);
			set => Set(value);
		}

		public double ThumbnailCacheSizeLimit
		{
			get => Get(512d);
			set => Set(value);
		}

		IReadOnlyList<DateTimeFormatRule> IDateTimeFormatRuleSource.ColumnFormatRules
			=> DateTimeColumnFormatRules;

		IReadOnlyList<DateTimeFormatRule> IDateTimeFormatRuleSource.DirectoryFormatRules
			=> DateTimeFormatDirectoryRules;

		DateTimeFormats IDateTimeFormatRuleSource.DefaultDateTimeFormat
			=> DateTimeFormat;

		string? IDateTimeFormatRuleSource.DefaultDateTimeFormatPattern
			=> DateTimeFormatCustomPattern;

		private List<DateTimeFormatRule> NormalizeColumnRules(List<DateTimeFormatRule>? rules)
		{
			if (rules is null)
				return [];

			var result = new List<DateTimeFormatRule>();
			var indexByColumn = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (var rule in rules.OfType<DateTimeFormatRule>())
			{
				var columnId = rule.Column?.ToString() ?? "*";

				if (indexByColumn.TryGetValue(columnId, out var index))
				{
					if (ShouldReplace(result[index], rule))
						result[index] = rule;
				}
				else
				{
					indexByColumn[columnId] = result.Count;
					result.Add(rule);
				}
			}

			return result;
		}

		private List<DateTimeFormatRule> NormalizeDirectoryRules(List<DateTimeFormatRule>? rules)
		{
			if (rules is null)
				return [];

			var result = new List<DateTimeFormatRule>();
			var indexByScope = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			foreach (var rule in rules.OfType<DateTimeFormatRule>())
			{
				var normalized = _volumePathProvider is null
					? rule
					: PortableDirectoryKey.NormalizeRule(rule, _volumePathProvider);

				var scopeId = GetRuleScopeId(normalized);

				if (indexByScope.TryGetValue(scopeId, out var index))
				{
					if (ShouldReplace(result[index], normalized))
						result[index] = normalized;
				}
				else
				{
					indexByScope[scopeId] = result.Count;
					result.Add(normalized);
				}
			}

			return result;
		}

		private static string GetRuleScopeId(DateTimeFormatRule rule)
		{
			var key = PortableDirectoryKey.FromRule(rule);
			var column = rule.Column?.ToString() ?? "*";

			return key.IsVolumeScoped
				? $"V|{key.VolumeId}|{key.RelativePath}|{column}"
				: $"A|{key.AbsolutePath}|{column}";
		}

		private static bool ShouldReplace(DateTimeFormatRule existing, DateTimeFormatRule candidate)
			=> candidate.Validate() is DateTimeFormatRuleValidationError.None ||
				existing.Validate() is not DateTimeFormatRuleValidationError.None;

		protected override void RaiseOnSettingChangedEvent(object sender, SettingChangedEventArgs e)
		{
			base.RaiseOnSettingChangedEvent(sender, e);
		}
	}
}
