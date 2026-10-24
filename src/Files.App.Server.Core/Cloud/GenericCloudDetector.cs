// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Server.Core.Cloud
{
	/// <summary>
	/// Provides an utility for generic cloud detection.
	/// </summary>
	internal sealed class GenericCloudDetector(ILogger logger) : AbstractCloudDetector(logger)
	{
		protected override async IAsyncEnumerable<ICloudProvider> GetProviders()
		{
			foreach (var provider in await CloudDrivesDetector.DetectCloudDrives(Logger))
			{
				yield return provider;
			}
		}
	}
}
