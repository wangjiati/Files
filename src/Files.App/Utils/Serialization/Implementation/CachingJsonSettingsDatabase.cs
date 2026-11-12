// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Files.App.Utils.Serialization.Implementation
{
	internal sealed class CachingJsonSettingsDatabase : DefaultJsonSettingsDatabase
	{
		private ConcurrentDictionary<string, JsonElement>? _settingsCache;

		private readonly object pendingGate = new();
		private bool hasPendingWrite;
		private int pendingVersion;
		private readonly SettingsFlushDebouncer flushDebouncer;

		public CachingJsonSettingsDatabase(
			ISettingsSerializer settingsSerializer,
			IJsonSettingsSerializer jsonSettingsSerializer,
			JsonSerializerContext jsonSerializerContext)
			: base(settingsSerializer, jsonSettingsSerializer, jsonSerializerContext)
		{
			flushDebouncer = new(HasPendingWrites, FlushPendingWrites);
		}

		public override TValue? GetValue<TValue>(string key, TValue? defaultValue = default) where TValue : default
		{
			_settingsCache ??= GetFreshSettings();

			if (_settingsCache.TryGetValue(key, out var objVal))
			{
				return GetValueFromElement<TValue>(objVal) ?? defaultValue;
			}
			else
			{
				var defaultElement = GetElementFromValue(defaultValue);
				// Persist cached defaults with the next settings change, not during reads.
				_settingsCache.TryAdd(key, defaultElement);

				return defaultValue;
			}
		}

		public override bool SetValue<TValue>(string key, TValue? newValue) where TValue : default
		{
			var newElement = GetElementFromValue(newValue);

			// Mutate the cache and mark pending atomically: ImportSettings replaces the
			// cache under the same gate, so a racing write can never be discarded by the
			// replacement or leave the pending flag referring to dropped content.
			lock (pendingGate)
			{
				_settingsCache ??= GetFreshSettings();

				if (_settingsCache.TryAdd(key, newElement))
				{
					MarkPendingWrite();
					return true;
				}

				if (JsonElement.DeepEquals(_settingsCache[key], newElement))
					return false;

				_settingsCache[key] = newElement;
				MarkPendingWrite();
				return true;
			}
		}

		public override bool RemoveKey(string key)
		{
			lock (pendingGate)
			{
				_settingsCache ??= GetFreshSettings();

				if (!_settingsCache.TryRemove(key, out _))
					return false;

				MarkPendingWrite();
				return true;
			}
		}

		public override bool FlushSettings()
		{
			return flushDebouncer.Flush();
		}

		public override bool ImportSettings(object? import)
		{
			// Imports replace the whole store. The whole import runs under the pending gate
			// so a concurrent SetValue/RemoveKey (serialized on the same gate) either
			// completes before the pre-import flush — and is replaced, as any pre-import
			// state must be — or lands on the imported cache afterwards and survives.
			// The pending flush is called directly instead of through the debouncer:
			// acquiring the debouncer's flush gate while holding the pending gate could
			// deadlock against a concurrent debounce flush.
			lock (pendingGate)
			{
				if (!FlushPendingWrites())
					// Pending writes could not be persisted; keep them pending and let the
					// caller retry rather than discarding them with the store replacement.
					return false;

				if (!base.ImportSettings(import))
					return false;

				_settingsCache = GetFreshSettings();

				return true;
			}
		}

		public override IDictionary<string, JsonElement> ExportSettings()
		{
			// Exports read from disk; flush first so pending writes are included. When the
			// flush fails, export the on-disk state and record it: pending values stay
			// pending and reach the next export or exit flush.
			if (!flushDebouncer.Flush())
			{
				App.Logger.LogWarning(
					"Could not flush pending settings writes to {SettingsFile} before export; the export reflects the last persisted state.",
					SettingsSerializer.FilePath);
			}

			return base.ExportSettings();
		}

		private void MarkPendingWrite()
		{
			lock (pendingGate)
			{
				hasPendingWrite = true;
				pendingVersion++;
			}

			flushDebouncer.ScheduleFlush();
		}

		private bool HasPendingWrites()
		{
			lock (pendingGate)
				return hasPendingWrite;
		}

		private bool FlushPendingWrites()
		{
			ConcurrentDictionary<string, JsonElement> cache;
			int version;

			lock (pendingGate)
			{
				if (!hasPendingWrite)
					return true;

				version = pendingVersion;
				cache = _settingsCache!;
			}

			bool saved;
			try
			{
				saved = SaveSettings(cache);
			}
			catch (Exception ex)
			{
				// Unexpected write failures must keep the value pending for the next flush;
				// the debounce timer would otherwise swallow the exception silently.
				App.Logger.LogWarning(ex, "Could not persist pending settings writes to {SettingsFile}.", SettingsSerializer.FilePath);
				saved = false;
			}

			lock (pendingGate)
			{
				// Clear pending only when the write succeeded and no new write arrived
				// during serialization; the debouncer then flushes the merged cache again.
				if (saved && pendingVersion == version)
					hasPendingWrite = false;
			}

			return saved;
		}
	}
}
