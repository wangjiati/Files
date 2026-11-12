// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Files.App.Utils.Serialization.Implementation
{
	/// <summary>
	/// Trailing debounce for settings persistence: reads observe pending values immediately
	/// while the merged persistence action runs once, 300ms after the last change.
	/// </summary>
	internal sealed class SettingsFlushDebouncer
	{
		public static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

		// Settings databases are app-lifetime singletons; the registry keeps exit-path
		// flush reachable without coupling the app shell to each database instance.
		private static readonly ConcurrentDictionary<SettingsFlushDebouncer, byte> instances = new();

		private readonly object gate = new();
		private readonly object flushGate = new();
		private readonly Func<bool> hasPendingWrites;
		private readonly Func<bool> flushPendingWrites;
		private readonly Timer flushTimer;

		public SettingsFlushDebouncer(Func<bool> hasPendingWrites, Func<bool> flushPendingWrites)
		{
			this.hasPendingWrites = hasPendingWrites;
			this.flushPendingWrites = flushPendingWrites;
			flushTimer = new Timer(static state => _ = ((SettingsFlushDebouncer)state!).Flush(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			instances.TryAdd(this, 0);
		}

		/// <summary>
		/// Records that a write is pending and restarts the trailing timer.
		/// </summary>
		public void ScheduleFlush()
		{
			lock (gate)
				flushTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
		}

		/// <summary>
		/// Persists all pending writes synchronously; data is durable once this returns true.
		/// Failed writes stay pending and are retried by the next change or explicit flush.
		/// </summary>
		public bool Flush()
		{
			lock (flushGate)
			{
				StopTimer();
				while (hasPendingWrites())
				{
					if (!flushPendingWrites())
						return false;

					StopTimer();
				}

				return true;
			}
		}

		/// <summary>
		/// Flushes every registered debouncer. Called on window teardown and process exit
		/// so no debounced terminal value is lost.
		/// </summary>
		public static bool FlushAll(TimeSpan? flushEnterTimeout = null)
		{
			var flushed = true;
			foreach (var instance in instances.Keys)
			{
				// A flush already in progress (e.g. the debounce timer pushing to the server)
				// must not stall process teardown indefinitely.
				if (!Monitor.TryEnter(instance.flushGate, flushEnterTimeout ?? Timeout.InfiniteTimeSpan))
				{
					flushed = false;
					continue;
				}

				try
				{
					flushed &= instance.Flush();
				}
				catch (Exception ex)
				{
					// One failing database must not starve the exit flush of the others.
					App.Logger.LogWarning(ex, "Could not flush a settings database during teardown.");
					flushed = false;
				}
				finally
				{
					Monitor.Exit(instance.flushGate);
				}
			}

			return flushed;
		}

		/// <summary>
		/// Disposes the debounce timer and removes the instance from the registry.
		/// Test teardown only: production databases are app-lifetime singletons that
		/// rely on the registry keeping exit-path flush reachable, so it never shrinks.
		/// </summary>
		internal void Dispose()
		{
			// Wait for an in-flight callback so it cannot fire on a disposed timer.
			using var callbacksDone = new ManualResetEvent(false);
			if (flushTimer.Dispose(callbacksDone))
				callbacksDone.WaitOne(TimeSpan.FromSeconds(5));

			instances.TryRemove(this, out _);
		}

		/// <summary>
		/// Disposes every registered debouncer's timer and clears the registry.
		/// Test teardown only.
		/// </summary>
		internal static void ResetForTests()
		{
			foreach (var instance in instances.Keys)
				instance.Dispose();
		}

		private void StopTimer()
		{
			lock (gate)
				flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		}
	}
}
