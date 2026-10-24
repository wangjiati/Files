// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Text.Json.Nodes;

namespace Files.App
{
	internal static class VersionedJsonFile
	{
		private const int CurrentSchemaVersion = 1;

		public static JsonNode? Read(string filePath, string? payloadName, bool allowUnversioned = false)
		{
			if (!File.Exists(filePath))
				return null;

			var text = File.ReadAllText(filePath);
			if (string.IsNullOrWhiteSpace(text))
				return null;

			JsonObject root;
			try
			{
				root = JsonNode.Parse(text) as JsonObject
					?? throw new InvalidDataException("The data file must contain a JSON object.");
			}
			catch (JsonException ex)
			{
				throw new InvalidDataException("The data file is malformed.", ex);
			}

			if (allowUnversioned && root["schemaVersion"] is null)
				return root.DeepClone();

			if (root["schemaVersion"] is not JsonValue schemaNode || !schemaNode.TryGetValue<int>(out var schemaVersion))
				throw new InvalidDataException("The data file schema version is invalid.");
			if (schemaVersion > CurrentSchemaVersion)
				throw new NotSupportedException($"Data schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
			if (schemaVersion != CurrentSchemaVersion)
				throw new InvalidDataException("The data file schema version is invalid.");

			if (payloadName is null)
			{
				root.Remove("schemaVersion");
				return root.DeepClone();
			}

			return root[payloadName]?.DeepClone()
				?? throw new InvalidDataException($"The data file must contain a '{payloadName}' value.");
		}

		public static void Write(string filePath, string? payloadName, JsonNode? payload)
		{
			JsonObject root;
			if (payloadName is null)
			{
				root = payload?.DeepClone() as JsonObject
					?? throw new InvalidDataException("The settings data must contain a JSON object.");
				root["schemaVersion"] = CurrentSchemaVersion;
			}
			else
			{
				root = new()
				{
					["schemaVersion"] = CurrentSchemaVersion,
					[payloadName] = payload,
				};
			}
			AtomicFile.WriteAllText(filePath, root.ToJsonString());
		}
	}
}
