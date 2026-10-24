// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Shell;

public static class ShellNewMenuHelper
{
	public static Task<IReadOnlyList<ShellNewEntry>> GetNewContextMenuEntries() =>
		Ioc.Default.GetRequiredService<IAppServerService>().GetShellNewEntriesAsync();

	public static Task<ShellNewEntry?> GetNewContextMenuEntryForType(string extension) =>
		Ioc.Default.GetRequiredService<IAppServerService>().GetShellNewEntryAsync(extension);
}
