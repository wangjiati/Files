// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Files.App
{
	internal sealed class JsonLocalStateStore : ILocalStateStore
	{
		private const int CurrentSchemaVersion = 1;

		private readonly string _filePath;
		private JsonObject _values;

		public JsonLocalStateStore(string filePath)
		{
			_filePath = Path.GetFullPath(filePath);
			_values = ReadValues(_filePath);
		}

		public bool GetBoolean(string key, bool defaultValue = false) =>
			_values[key] is JsonNode value ? value.GetValue<bool>() : defaultValue;

		public void SetBoolean(string key, bool value) => Update(key, JsonValue.Create(value));

		public int GetInt32(string key, int defaultValue = 0) =>
			_values[key] is JsonNode value ? value.GetValue<int>() : defaultValue;

		public void SetInt32(string key, int value) => Update(key, JsonValue.Create(value));

		public double GetDouble(string key, double defaultValue = 0d) =>
			_values[key] is JsonNode value ? value.GetValue<double>() : defaultValue;

		public void SetDouble(string key, double value) => Update(key, JsonValue.Create(value));

		public string? GetString(string key, string? defaultValue = null) =>
			_values[key] is JsonNode value ? value.GetValue<string>() : defaultValue;

		public void SetString(string key, string value) => Update(key, JsonValue.Create(value));

		public void Remove(string key)
		{
			if (!_values.ContainsKey(key))
				return;

			var updated = CopyValues();
			updated.Remove(key);
			Persist(updated);
			_values = updated;
		}

		private void Update(string key, JsonNode? value)
		{
			var updated = CopyValues();
			updated[key] = value;
			Persist(updated);
			_values = updated;
		}

		private JsonObject CopyValues() => _values.DeepClone().AsObject();

		private void Persist(JsonObject values)
		{
			var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("The local state file must have a parent directory.");
			Directory.CreateDirectory(directory);

			var root = new JsonObject
			{
				["schemaVersion"] = CurrentSchemaVersion,
				["values"] = values,
			};
			var json = root.ToJsonString();
			var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

			try
			{
				using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
						writer.Write(json);
					stream.Flush(true);
				}

				File.Move(temporaryPath, _filePath, true);
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		private static JsonObject ReadValues(string filePath)
		{
			if (!File.Exists(filePath))
				return [];

			try
			{
				var root = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject
					?? throw new InvalidDataException("The local state file must contain a JSON object.");
				if (root["schemaVersion"] is not JsonValue schemaNode || !schemaNode.TryGetValue<int>(out var schemaVersion))
					throw new InvalidDataException("The local state schema version is invalid.");
				if (schemaVersion > CurrentSchemaVersion)
					throw new NotSupportedException($"Local state schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
				if (schemaVersion != CurrentSchemaVersion)
					throw new InvalidDataException("The local state schema version is invalid.");

				return root["values"] as JsonObject
					?? throw new InvalidDataException("The local state values must contain a JSON object.");
			}
			catch (JsonException ex)
			{
				throw new InvalidDataException("The local state file is malformed.", ex);
			}
		}
	}
}
