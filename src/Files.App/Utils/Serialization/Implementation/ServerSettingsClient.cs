// Copyright (c) Files Community
// Licensed under the MIT License.

#if DISTRIBUTION_MSIX
using Files.App.Server;

namespace Files.App.Utils.Serialization.Implementation;

/// <summary>
/// Seam isolating the static WinRT server projection so ServerSettingsDatabase's logic
/// (pending-write dedup, change overlay, import ordering) is unit-testable in-process.
/// </summary>
internal interface IServerSettingsClient
{
	SettingsValue[] GetSettings(string fileName);

	bool SetSetting(string fileName, string key, SettingsValue[] value);

	bool RemoveSetting(string fileName, string key);

	bool FlushSettings(string fileName);

	bool ImportSettings(string fileName, SettingsValue[] values);
}

internal sealed class WinRtServerSettingsClient : IServerSettingsClient
{
	public SettingsValue[] GetSettings(string fileName) => Server.SettingsService.GetSettings(fileName);

	public bool SetSetting(string fileName, string key, SettingsValue[] value) => Server.SettingsService.SetSetting(fileName, key, value);

	public bool RemoveSetting(string fileName, string key) => Server.SettingsService.RemoveSetting(fileName, key);

	public bool FlushSettings(string fileName) => Server.SettingsService.FlushSettings(fileName);

	public bool ImportSettings(string fileName, SettingsValue[] values) => Server.SettingsService.ImportSettings(fileName, values);
}
#endif
