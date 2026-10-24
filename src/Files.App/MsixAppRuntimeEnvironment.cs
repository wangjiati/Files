// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Files.App
{
	internal sealed record MsixAppRuntimeSnapshot(
		string ProductId,
		string DisplayName,
		Version Version,
		string InstallDirectory,
		string DataDirectory,
		string CacheDirectory,
		string PackageFamilyName);

	internal sealed class MsixAppRuntimeEnvironment : IAppRuntimeEnvironment
	{
		public DistributionKind Distribution => DistributionKind.Msix;
		public string ProductId { get; }
		public string DisplayName { get; }
		public Version Version { get; }
		public string InstallDirectory { get; }
		public string DataDirectory { get; }
		public string SettingsDirectory { get; }
		public string LogDirectory { get; }
		public string CacheDirectory { get; }
		public string? PackageFamilyName { get; }
		public string InstanceScope { get; }
		public bool SupportsSystemIntegration => true;

		public MsixAppRuntimeEnvironment()
			: this(ReadSnapshot)
		{
		}

		internal MsixAppRuntimeEnvironment(Func<MsixAppRuntimeSnapshot> readSnapshot)
		{
			var snapshot = readSnapshot();
			ProductId = snapshot.ProductId;
			DisplayName = snapshot.DisplayName;
			Version = snapshot.Version;
			InstallDirectory = snapshot.InstallDirectory;
			DataDirectory = snapshot.DataDirectory;
			SettingsDirectory = Path.Combine(DataDirectory, Constants.LocalSettings.SettingsFolderName);
			LogDirectory = DataDirectory;
			CacheDirectory = snapshot.CacheDirectory;
			PackageFamilyName = snapshot.PackageFamilyName;
			InstanceScope = snapshot.PackageFamilyName;
		}

		private static MsixAppRuntimeSnapshot ReadSnapshot()
		{
			var package = Package.Current;
			var applicationData = ApplicationData.Current;
			var packageVersion = package.Id.Version;

			return new(
				package.Id.Name,
				package.DisplayName,
				new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision),
				package.InstalledLocation.Path,
				applicationData.LocalFolder.Path,
				applicationData.LocalCacheFolder.Path,
				package.Id.FamilyName);
		}
	}
}
