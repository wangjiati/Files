// Copyright (c) Files Community
// Licensed under the MIT License.

#if DISTRIBUTION_MSIX
using Files.App.Server;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization.Metadata;

namespace Files.App.Utils.Serialization.Implementation;

internal sealed class ServerSettingsDatabase : IJsonSettingsDatabase
{
	private readonly string fileName;
	private readonly IJsonSettingsSerializer serializer;
	private readonly JsonSerializerContext context;
	private readonly Dictionary<string, JsonElement> defaults = [];
	private readonly Lock gate = new();
	// Null Data removes a key; a JSON null remains a stored value (mirrors the server store).
	private readonly record struct PendingSettingChange(long Version, JsonElement? Data);
	private readonly Dictionary<string, PendingSettingChange> pendingWrites = new(StringComparer.Ordinal);
	private long pendingVersion;
	private readonly SettingsFlushDebouncer flushDebouncer;
	private Dictionary<string, JsonElement> cache;
	private int cacheVersion;
	internal event Action<string, JsonElement>? Changed;

	public ServerSettingsDatabase(string fileName, IJsonSettingsSerializer serializer, JsonSerializerContext context)
	{
		this.fileName = fileName;
		this.serializer = serializer;
		this.context = context;
		cache = ReadValues();
		flushDebouncer = new(HasPendingWrites, PushPendingWritesToServer);
		App.ServerChanged += OnServerChanged;
	}

	private Dictionary<string, JsonElement> ReadValues() => SettingsValueCodec.DecodeObject(Server.SettingsService.GetSettings(fileName));

	public TValue? GetValue<TValue>(string key, TValue? defaultValue = default)
	{
		lock (gate)
		{
			if (cache.TryGetValue(key, out var value))
				return GetValueFromElement<TValue>(value) ?? defaultValue;
			if (defaults.TryGetValue(key, out var fallback))
				return GetValueFromElement<TValue>(fallback) ?? defaultValue;
			defaults[key] = GetElementFromValue(defaultValue);
			return defaultValue;
		}
	}

	public bool SetValue<TValue>(string key, TValue? value)
	{
		var element = GetElementFromValue(value);
		bool changed;
		lock (gate)
		{
			changed = !cache.TryGetValue(key, out var old) || !JsonElement.DeepEquals(old, element);
			cache[key] = element;
			cacheVersion++;
			defaults.Remove(key);
			if (changed)
				pendingWrites[key] = new(++pendingVersion, element);
		}

		if (changed)
			flushDebouncer.ScheduleFlush();

		// The server result is not available synchronously anymore; the local cache
		// comparison decides, which is equivalent absent concurrent writers.
		return changed;
	}

	public bool RemoveKey(string key)
	{
		bool removed;
		lock (gate)
		{
			removed = cache.Remove(key);
			if (removed)
			{
				cacheVersion++;
				pendingWrites[key] = new(++pendingVersion, null);
			}
		}

		if (removed)
			flushDebouncer.ScheduleFlush();

		return removed;
	}

	public bool ImportSettings(object? import)
	{
		var values = import switch
		{
			IDictionary<string, JsonElement> elements => elements,
			IDictionary<string, object?> objects => objects.ToDictionary(pair => pair.Key, pair => GetElementFromValue(pair.Value)),
			_ => null,
		};
		if (values is null)
			return false;

		// The import replaces the whole store; flush first so stale pending writes
		// cannot be pushed over imported values afterwards.
		flushDebouncer.Flush();

		var changed = Server.SettingsService.ImportSettings(fileName, SettingsValueCodec.EncodeObject(values));
		var flushed = FlushSettings();
		var fresh = ReadValues();
		lock (gate)
		{
			cache = fresh;
			cacheVersion++;
		}
		return changed && flushed;
	}

	public IDictionary<string, JsonElement> ExportSettings()
	{
		// Exports read from the server; flush first so pending writes are included.
		flushDebouncer.Flush();

		return ReadValues();
	}

	public bool FlushSettings() => flushDebouncer.Flush() && Server.SettingsService.FlushSettings(fileName);

	private bool HasPendingWrites()
	{
		lock (gate)
			return pendingWrites.Count > 0;
	}

	private bool PushPendingWritesToServer()
	{
		while (true)
		{
			KeyValuePair<string, PendingSettingChange>[] batch;
			lock (gate)
			{
				if (pendingWrites.Count == 0)
					return true;

				batch = [.. pendingWrites];
			}

			try
			{
				foreach (var op in batch)
				{
					if (op.Value.Data is { } element)
						Server.SettingsService.SetSetting(fileName, op.Key, SettingsValueCodec.Encode(element));
					else
						Server.SettingsService.RemoveSetting(fileName, op.Key);

					lock (gate)
					{
						// A newer local edit for the same key must stay pending.
						if (pendingWrites.TryGetValue(op.Key, out var current) && current.Version == op.Value.Version)
							pendingWrites.Remove(op.Key);
					}
				}
			}
			catch (Exception ex)
			{
				// Unsent writes remain pending and are retried by the next change or flush.
				App.Logger.LogWarning(ex, "Could not push pending settings writes for {SettingsFileName}.", fileName);
				return false;
			}

			lock (gate)
			{
				if (pendingWrites.Count == 0)
					return true;
			}
		}
	}

	private JsonTypeInfo<T?> GetTypeInfo<T>() => context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T?>
		?? throw new InvalidOperationException($"JSON serialization metadata is missing for {typeof(T)}.");

	private JsonElement GetElementFromValue<T>(T? value) => serializer.SerializeToElement(value, GetTypeInfo<T>());

	private T? GetValueFromElement<T>(JsonElement element)
	{
		try
		{
			return serializer.DeserializeFromElement(element, GetTypeInfo<T>());
		}
		catch (JsonException)
		{
			return default;
		}
	}

	private async void OnServerChanged(object? sender, string name)
	{
		if (name != ServerChangeNames.Settings(fileName))
			return;
		await SafetyExtensions.IgnoreExceptions(async () =>
		{
			var differences = await Task.Run(() =>
			{
				while (true)
				{
					int version;
					lock (gate)
						version = cacheVersion;
					var fresh = ReadValues();
					lock (gate)
					{
						if (version != cacheVersion)
							continue;
						// Re-apply local writes not yet pushed to the server, mirroring the
						// server's own read merge, so pending values are never reverted.
						foreach (var op in pendingWrites)
						{
							if (op.Value.Data is { } element)
								fresh[op.Key] = element;
							else
								fresh.Remove(op.Key);
						}
						var previous = cache;
						cache = fresh;
						cacheVersion++;
						var changes = fresh.Where(pair => !previous.TryGetValue(pair.Key, out var old) || !JsonElement.DeepEquals(old, pair.Value)).ToList();
						changes.AddRange(previous.Keys.Except(fresh.Keys).Select(key => new KeyValuePair<string, JsonElement>(key, default)));
						return changes.ToArray();
					}
				}
			});
			foreach (var pair in differences)
				Changed?.Invoke(pair.Key, pair.Value);
		}, App.Logger);
	}
}
#endif
