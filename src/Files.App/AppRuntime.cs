// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App
{
	public static class AppRuntime
	{
		private static IAppRuntimeEnvironment? _current;
		private static ILocalStateStore? _localState;

		public static IAppRuntimeEnvironment Current =>
			_current ?? throw new InvalidOperationException("The application runtime has not been initialized.");

		public static ILocalStateStore LocalState =>
			_localState ?? throw new InvalidOperationException("The application runtime has not been initialized.");

		public static void Initialize(DistributionKind distribution, string? portableRoot = null)
		{
			_current = distribution switch
			{
				DistributionKind.Msix => new MsixAppRuntimeEnvironment(),
				DistributionKind.Portable => new PortableAppRuntimeEnvironment(portableRoot),
				_ => throw new ArgumentOutOfRangeException(nameof(distribution), distribution, null),
			};
			_localState = distribution is DistributionKind.Msix
				? new ApplicationDataLocalStateStore()
				: new JsonLocalStateStore(System.IO.Path.Combine(Current.DataDirectory, "local-state.json"));
		}

		internal static void Reset()
		{
			_localState = null;
			_current = null;
		}
	}
}
