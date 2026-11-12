// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Helpers
{
	/// <summary>
	/// Wraps the UI-thread <see cref="SynchronizationContext"/> for the remainder of window
	/// teardown: an exception escaping a posted continuation fails the process inside
	/// CoreMessagingXP (ui-xaml#10447 regression, still in WASDK 2.6.5), skipping teardown
	/// finally blocks and the ProcessExit settings flush. Logging and swallowing keeps the
	/// teardown pump alive; installed once when the main window closes and never removed.
	/// </summary>
	internal sealed class TeardownSafeSynchronizationContext : SynchronizationContext
	{
		private readonly SynchronizationContext original;

		public TeardownSafeSynchronizationContext(SynchronizationContext original)
		{
			this.original = original;
		}

		public override void Post(SendOrPostCallback d, object? state)
		{
			try
			{
				// The wrapped callback runs where the original context would have run d;
				// enqueue ordering and thread affinity are untouched.
				original.Post(Wrapped, state);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to enqueue a teardown continuation.");
			}

			void Wrapped(object? wrappedState)
			{
				try
				{
					d(wrappedState);
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, "Swallowed an exception from a teardown continuation.");
				}
			}
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			// The WinUI context always throws NotSupportedException; keep that behavior.
			original.Send(d, state);
		}

		public override SynchronizationContext CreateCopy()
		{
			return new TeardownSafeSynchronizationContext(original.CreateCopy());
		}
	}
}
