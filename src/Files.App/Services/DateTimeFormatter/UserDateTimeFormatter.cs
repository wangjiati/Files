// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;

namespace Files.App.Services.DateTimeFormatter
{
	internal interface IDateTimeFormatSettingsSource
	{
		DateTimeFormats DateTimeFormat { get; }

		string? DateTimeFormatCustomPattern { get; }

		event EventHandler<SettingChangedEventArgs> OnSettingChangedEvent;
	}

	internal sealed class UserSettingsDateTimeFormatSource : IDateTimeFormatSettingsSource
	{
		private readonly IUserSettingsService _settingsService;

		public UserSettingsDateTimeFormatSource(IUserSettingsService settingsService)
		{
			_settingsService = settingsService;
		}

		public DateTimeFormats DateTimeFormat
			=> _settingsService.GeneralSettingsService.DateTimeFormat;

		public string? DateTimeFormatCustomPattern
			=> _settingsService.GeneralSettingsService.DateTimeFormatCustomPattern;

		public event EventHandler<SettingChangedEventArgs> OnSettingChangedEvent
		{
			add => _settingsService.OnSettingChangedEvent += value;
			remove => _settingsService.OnSettingChangedEvent -= value;
		}
	}

	internal sealed class UserDateTimeFormatter : IDateTimeFormatter
	{
		private readonly IDateTimeFormatSettingsSource _settingsSource;
		private readonly IDateTimeFormatterFactory _formatterFactory;

		private IDateTimeFormatter formatter = null!;

		public string Name
			=> formatter.Name;

		public UserDateTimeFormatter()
			: this(
				new UserSettingsDateTimeFormatSource(Ioc.Default.GetRequiredService<IUserSettingsService>()),
				Ioc.Default.GetRequiredService<IDateTimeFormatterFactory>())
		{
		}

		internal UserDateTimeFormatter(
			IDateTimeFormatSettingsSource settingsSource,
			IDateTimeFormatterFactory formatterFactory)
		{
			_settingsSource = settingsSource;
			_formatterFactory = formatterFactory;
			_settingsSource.OnSettingChangedEvent += UserSettingsService_OnSettingChangedEvent;

			Update();
		}

		public string ToShortLabel(DateTimeOffset offset)
			=> formatter.ToShortLabel(offset);

		public string ToLongLabel(DateTimeOffset offset)
			=> formatter.ToLongLabel(offset);

		public ITimeSpanLabel ToTimeSpanLabel(DateTimeOffset offset, GroupByDateUnit unit)
			=> formatter.ToTimeSpanLabel(offset, unit);

		private void Update()
		{
			formatter = _formatterFactory.GetDateTimeFormatter(
				_settingsSource.DateTimeFormat,
				_settingsSource.DateTimeFormatCustomPattern);
		}

		private void UserSettingsService_OnSettingChangedEvent(object? sender, SettingChangedEventArgs e)
		{
			if (e.SettingName is nameof(IGeneralSettingsService.DateTimeFormat) or
				nameof(IGeneralSettingsService.DateTimeFormatCustomPattern))
			{
				Update();
			}
		}
	}
}
