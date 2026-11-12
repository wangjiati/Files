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
			_settingsCache ??= GetFreshSettings();
			var newElement = GetElementFromValue(newValue);

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

		public override bool RemoveKey(string key)
		{
			_settingsCache ??= GetFreshSettings();

			if (!_settingsCache.TryRemove(key, out _))
				return false;

			MarkPendingWrite();
			return true;
		}

		public override bool FlushSettings()
		{
			return flushDebouncer.Flush();
		}

		public override bool ImportSettings(object? import)
		{
			// Imports replace the whole store; flush first so a failed import cannot lose pending writes.
			flushDebouncer.Flush();

			if (base.ImportSettings(import))
			{
				// Take the replacement under the pending gate so a concurrent flush cannot
				// serialize the pre-import cache over the imported data.
				lock (pendingGate)
					_settingsCache = GetFreshSettings();

				return true;
			}

			return false;
		}

		public override IDictionary<string, JsonElement> ExportSettings()
		{
			// Exports read from disk; flush first so pending writes are included.
			flushDebouncer.Flush();

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
