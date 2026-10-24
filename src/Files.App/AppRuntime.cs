// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App
{
	public static class AppRuntime
	{
		private static IAppRuntimeEnvironment? _current;

		public static IAppRuntimeEnvironment Current =>
			_current ?? throw new InvalidOperationException("The application runtime has not been initialized.");

		public static void Initialize(DistributionKind distribution, string? portableRoot = null)
		{
			_current = distribution switch
			{
				DistributionKind.Msix => new MsixAppRuntimeEnvironment(),
				DistributionKind.Portable => new PortableAppRuntimeEnvironment(portableRoot),
				_ => throw new ArgumentOutOfRangeException(nameof(distribution), distribution, null),
			};
		}
	}
}
