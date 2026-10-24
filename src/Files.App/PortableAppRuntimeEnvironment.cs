// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Files.App
{
	internal sealed class PortableAppRuntimeEnvironment : IAppRuntimeEnvironment
	{
		public DistributionKind Distribution => DistributionKind.Portable;
		public string ProductId => "Files.Internal";
		public string DisplayName => "Files - Internal";
		public Version Version { get; }
		public string InstallDirectory { get; }
		public string DataDirectory { get; }
		public string SettingsDirectory { get; }
		public string LogDirectory { get; }
		public string CacheDirectory { get; }
		public string? PackageFamilyName => null;
		public string InstanceScope { get; }
		public bool SupportsSystemIntegration => false;

		public PortableAppRuntimeEnvironment(string? portableRoot = null)
		{
			InstallDirectory = Path.GetFullPath(portableRoot ?? AppContext.BaseDirectory);
			DataDirectory = Path.Combine(InstallDirectory, "Data");
			SettingsDirectory = Path.Combine(DataDirectory, Constants.LocalSettings.SettingsFolderName);
			LogDirectory = Path.Combine(DataDirectory, "logs");
			CacheDirectory = Path.Combine(DataDirectory, "cache");
			Version = typeof(PortableAppRuntimeEnvironment).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

			var scopePath = Path.TrimEndingDirectorySeparator(InstallDirectory).ToUpperInvariant();
			InstanceScope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scopePath)));
		}
	}
}
