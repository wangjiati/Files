// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App
{
	public enum DistributionKind
	{
		Msix,
		Portable,
	}

	public interface IAppRuntimeEnvironment
	{
		DistributionKind Distribution { get; }
		string ProductId { get; }
		string DisplayName { get; }
		Version Version { get; }
		string InstallDirectory { get; }
		string DataDirectory { get; }
		string SettingsDirectory { get; }
		string LogDirectory { get; }
		string CacheDirectory { get; }
		string? PackageFamilyName { get; }
		string InstanceScope { get; }
		bool SupportsSystemIntegration { get; }
	}
}
