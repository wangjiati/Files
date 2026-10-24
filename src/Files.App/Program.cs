// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Settings;
using Microsoft.Windows.AppLifecycle;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.ApplicationModel.Activation;

namespace Files.App
{
	/// <summary>
	/// Represents the base entry point of the Files app.
	/// </summary>
	/// <remarks>
	/// Gets called at the first time when the app launched or activated.
	/// </remarks>
	internal sealed class Program
	{
		private const string LaunchCwdKey = "LastLaunchCwd";

#if DISTRIBUTION_PORTABLE
		private static PortableDataLease? _portableDataLease;
#endif

		private static string InstanceKeyPrefix =>
			$"{AppRuntime.Current.ProductId}.{AppRuntime.Current.InstanceScope}.Window.";

		/// <summary>
		/// Reads and clears the working directory captured by the source process
		/// in <see cref="Main"/>, falling back to the current process's working
		/// directory if no value is pending. (#16982)
		/// </summary>
		public static string ConsumeLaunchCwd()
		{
			var cwd = AppRuntime.LocalState.GetString(LaunchCwdKey);
			if (cwd is not null)
				AppRuntime.LocalState.Remove(LaunchCwdKey);
			return string.IsNullOrEmpty(cwd) ? Environment.CurrentDirectory : cwd;
		}

		/// <summary>
		/// Initializes the process; the entry point of the process.
		/// </summary>
		/// <remarks>
		/// <see cref="Main"/> cannot be declared to be async because this prevents Narrator from reading XAML elements in a WinUI app.
		/// </remarks>
		[STAThread]
		private static void Main()
		{
#if DISTRIBUTION_PORTABLE
			AppRuntime.Initialize(DistributionKind.Portable);
#else
			AppRuntime.Initialize(DistributionKind.Msix);
#endif
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			WinRT.ComWrappersSupport.InitializeComWrappers();

#if DISTRIBUTION_PORTABLE
			try
			{
				_portableDataLease = PreparePortableData();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				ShowPortableDataError(ex);
				return;
			}
#endif
			AppRuntime.LocalState.SetString(LaunchCwdKey, Environment.CurrentDirectory);

			var OpenTabInExistingInstance = AppRuntime.LocalState.GetBoolean("OpenTabInExistingInstance", true);

			AppActivationArguments activatedArgs;
			try
			{
				activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
			}
			catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154))
			{
				Windows.Win32.PInvoke.MessageBox(
					default,
					Constants.Startup.MissingRuntimeMessage,
					Constants.Startup.MissingRuntimeTitle,
					MESSAGEBOX_STYLE.MB_ICONERROR);

				throw new InvalidOperationException(Constants.Startup.MissingRuntimeMessage, ex);
			}

			var commandLineArgs = GetCommandLineArgs(activatedArgs);
			var isNewWindowLaunch = activatedArgs.Data is ILaunchActivatedEventArgs newWindowArgs &&
				newWindowArgs.Arguments is not null &&
				CommandLineParser.SplitArguments(newWindowArgs.Arguments, true).Contains(AppProcessLauncher.NewWindowArgument);

			if (!isNewWindowLaunch && commandLineArgs is not null)
			{
				var parsedCommands = CommandLineParser.ParseUntrustedCommands(commandLineArgs);

				if (parsedCommands is not null)
				{
					foreach (var command in parsedCommands)
					{
						switch (command.Type)
						{
							case ParsedCommandType.ExplorerShellCommand:
								if (!ShellHelpers.IsSupportedShellPath(command.Payload))
								{
									OpenShellCommandInExplorer(command.Payload, Environment.ProcessId);
									return;
								}
								break;

							default:
								break;
						}
					}
				}

				// Always open a new instance for OpenDialog, never open new instance for "-Tag" command
				if (parsedCommands is null || !parsedCommands.Any(x => x.Type == ParsedCommandType.OutputPath) &&
					(OpenTabInExistingInstance || parsedCommands.Any(x => x.Type == ParsedCommandType.TagFiles)))
				{
					if (TryRedirectActivationToExistingInstance(activatedArgs))
						return;
				}
			}
			else if (activatedArgs.Data is ILaunchActivatedEventArgs tileArgs)
			{
				if (tileArgs.Arguments is not null &&
					FileExtensionHelpers.IsExecutableFile(tileArgs.Arguments))
				{
					if (File.Exists(tileArgs.Arguments))
					{
						OpenFileFromTile(tileArgs.Arguments);
						return;
					}
				}
			}

			if (!isNewWindowLaunch && OpenTabInExistingInstance && commandLineArgs is null)
			{
				if (activatedArgs.Data is ILaunchActivatedEventArgs launchArgs)
				{
					if (!string.IsNullOrWhiteSpace(launchArgs.Arguments) && TryRedirectActivationToExistingInstance(activatedArgs))
						return;
				}
				else if (activatedArgs.Data is IProtocolActivatedEventArgs protocolArgs)
				{
					var parsedArgs = protocolArgs.Uri.Query.TrimStart('?').Split('=');
					if (parsedArgs.Length == 1)
					{
						if (TryRedirectActivationToExistingInstance(activatedArgs))
							return;
					}
				}
				else if (activatedArgs.Data is IFileActivatedEventArgs)
				{
					if (TryRedirectActivationToExistingInstance(activatedArgs))
						return;
				}
			}

			if (activatedArgs.Data is not IStartupTaskActivatedEventArgs)
				RegisterCurrentInstance();

			// Optional XAML performance changes must be opted into before XAML initialization
			XamlOptionalChanges.EnableChange(XamlChangeId.DefaultStyleOptimizations);
			XamlOptionalChanges.EnableChange(XamlChangeId.OptimizeApplyStyles);
			XamlOptionalChanges.EnableChange(XamlChangeId.IconNoGridOptimization);
			XamlOptionalChanges.EnableChange(XamlChangeId.DeferContextFlyoutInit);

			Application.Start((p) =>
			{
				var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
				SynchronizationContext.SetSynchronizationContext(context);

				_ = new App();
			});
		}

		private static PortableDataLease PreparePortableData()
		{
			var environment = AppRuntime.Current;
			Directory.CreateDirectory(environment.DataDirectory);
			Directory.CreateDirectory(environment.SettingsDirectory);
			Directory.CreateDirectory(environment.LogDirectory);
			Directory.CreateDirectory(environment.CacheDirectory);

			var lease = PortableDataLease.Acquire(environment.DataDirectory);
			var probePath = Path.Combine(environment.DataDirectory, $".{Guid.NewGuid():N}.write-probe");
			try
			{
				using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					probe.WriteByte(0);
					probe.Flush(true);
				}
				File.Delete(probePath);
				return lease;
			}
			catch
			{
				lease.Dispose();
				throw;
			}
		}

		private static void ShowPortableDataError(Exception exception)
		{
			var resourceManager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
			var message = resourceManager.MainResourceMap.TryGetSubtree("Resources")?.TryGetValue(
				"PortableDataStartupFailed",
				resourceManager.CreateResourceContext())?.ValueAsString;
			message = string.IsNullOrEmpty(message)
				? exception.Message
				: string.Format(message, AppRuntime.Current.DataDirectory, exception.Message);

			Windows.Win32.PInvoke.MessageBox(
				default,
				message,
				Constants.Startup.MissingRuntimeTitle,
				MESSAGEBOX_STYLE.MB_ICONERROR);
		}

		internal static void ReleasePortableDataLease()
		{
#if DISTRIBUTION_PORTABLE
			_portableDataLease?.Dispose();
			_portableDataLease = null;
#endif
		}

		internal static void RestorePortableDataLease()
		{
#if DISTRIBUTION_PORTABLE
			_portableDataLease ??= PreparePortableData();
#endif
		}

		internal static void RegisterCurrentInstance()
		{
			var instance = AppInstance.FindOrRegisterForKey($"{InstanceKeyPrefix}{Environment.ProcessId}");
			if (instance.IsCurrent)
				instance.Activated += OnActivated;
		}

		/// <summary>
		/// Gets command line args from AppActivationArguments
		/// Command line args can be ILaunchActivatedEventArgs, ICommandLineActivatedEventArgs or IProtocolActivatedEventArgs
		/// </summary>
		private static string? GetCommandLineArgs(AppActivationArguments activatedArgs)
		{
			// WINUI3: When launching from commandline the argument is not ICommandLineActivatedEventArgs (#10370)
			var cmdLaunchArgs = activatedArgs.Data is ILaunchActivatedEventArgs launchArgs &&
				launchArgs.Arguments is not null &&
				CommandLineParser.SplitArguments(launchArgs.Arguments, true).FirstOrDefault() is string arg0 &&
				(arg0.EndsWith($"{AppProcessLauncher.ProtocolScheme}.exe", StringComparison.OrdinalIgnoreCase) ||
				arg0.EndsWith(AppProcessLauncher.ProtocolScheme, StringComparison.OrdinalIgnoreCase) ||
				arg0.Equals(Path.Join(AppRuntime.Current.InstallDirectory, "Files.exe"), StringComparison.OrdinalIgnoreCase)) ? launchArgs.Arguments : null;
			var cmdProtocolArgs = activatedArgs.Data is IProtocolActivatedEventArgs protocolArgs &&
				protocolArgs.Uri.Query.TrimStart('?').Split('=') is string[] parsedArgs &&
				parsedArgs.Length == 2 && parsedArgs[0] == "cmd" ? Uri.UnescapeDataString(parsedArgs[1]) : null;
			var cmdLineArgs = activatedArgs.Data is ICommandLineActivatedEventArgs cmdArgs ? cmdArgs.Operation.Arguments : null;

			return cmdLaunchArgs ?? cmdProtocolArgs ?? cmdLineArgs;
		}

		/// <summary>
		/// Gets invoked when the application is activated.
		/// </summary>
		private static async void OnActivated(object? sender, AppActivationArguments args)
		{
			// WINUI3: Verify if needed or OnLaunched is called
			if (App.Current is App thisApp)
				await thisApp.OnActivatedAsync(args);
		}

		/// <summary>
		/// Attempts redirection, allowing a normal launch if the existing instance has exited.
		/// </summary>
		private static bool TryRedirectActivationToExistingInstance(AppActivationArguments args)
		{
			try
			{
				var instance = AppInstance.GetInstances().FirstOrDefault(instance =>
					!instance.IsCurrent && instance.Key.StartsWith(InstanceKeyPrefix, StringComparison.Ordinal));
				if (instance is null)
					return false;

				instance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				return false;
			}
		}

		public static void OpenShellCommandInExplorer(string shellCommand, int pid)
		{
			Win32Helper.OpenFolderInExistingShellWindow(shellCommand);
		}

		public static void OpenFileFromTile(string filePath)
		{
			LaunchHelper.LaunchAppAsync(filePath, null, null).Wait();
		}
	}
}
