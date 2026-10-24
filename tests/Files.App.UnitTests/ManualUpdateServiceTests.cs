// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class ManualUpdateServiceTests
	{
		[TestMethod]
		public async Task AllOperations_AreLocalNoOpsWithoutNetworkRequests()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			var requests = 0;
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var proxyTask = RespondToProxyRequestsAsync(listener, () => requests++, timeout.Token);
			var proxyUrl = $"http://127.0.0.1:{port}";
			var oldHttpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
			var oldHttpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
			var oldNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");

			try
			{
				Environment.SetEnvironmentVariable("HTTP_PROXY", proxyUrl);
				Environment.SetEnvironmentVariable("HTTPS_PROXY", proxyUrl);
				Environment.SetEnvironmentVariable("NO_PROXY", string.Empty);
				var service = new ManualUpdateService();

				await service.CheckForReleaseNotesAsync().WaitAsync(timeout.Token);
				await service.CheckForUpdatesAsync().WaitAsync(timeout.Token);
				await service.DownloadUpdatesAsync().WaitAsync(timeout.Token);
				await service.DownloadMandatoryUpdatesAsync().WaitAsync(timeout.Token);
				await service.CheckAndUpdateFilesLauncherAsync().WaitAsync(timeout.Token);

				Assert.IsFalse(service.IsUpdateAvailable);
				Assert.IsFalse(service.IsUpdating);
				Assert.AreEqual(0, service.UpdateProgress);
				Assert.IsFalse(service.AreReleaseNotesAvailable);
				Assert.AreEqual(0, requests);
			}
			finally
			{
				Environment.SetEnvironmentVariable("HTTP_PROXY", oldHttpProxy);
				Environment.SetEnvironmentVariable("HTTPS_PROXY", oldHttpsProxy);
				Environment.SetEnvironmentVariable("NO_PROXY", oldNoProxy);
				timeout.Cancel();
				listener.Stop();
				await Task.WhenAny(proxyTask, Task.Delay(100));
			}
		}

		private static async Task RespondToProxyRequestsAsync(TcpListener listener, Action onRequest, CancellationToken cancellationToken)
		{
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					using var client = await listener.AcceptTcpClientAsync(cancellationToken);
					onRequest();
					var response = Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
					await client.GetStream().WriteAsync(response, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (SocketException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
			{
			}
		}
	}
}
