// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.App.Server;

internal static class Program
{
	internal static readonly TaskCompletionSource<bool> ExitSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal static readonly ILogger Logger = NullLogger.Instance;
}
