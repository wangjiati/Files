// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;

namespace Files.App.Utils.Serialization
{
	internal sealed class GridLengthJsonConverter : JsonConverter<GridLength>
	{
		public override GridLength Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using var document = JsonDocument.ParseValue(ref reader);
			var root = document.RootElement;
			var value = root.TryGetProperty("Value", out var valueElement) ? valueElement.GetDouble() : 1d;
			var unitType = root.TryGetProperty("GridUnitType", out var unitTypeElement)
				? (GridUnitType)unitTypeElement.GetInt32()
				: GridUnitType.Auto;
			return new(value, unitType);
		}

		public override void Write(Utf8JsonWriter writer, GridLength value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteNumber("Value", value.Value);
			writer.WriteNumber("GridUnitType", (int)value.GridUnitType);
			writer.WriteBoolean("IsAbsolute", value.IsAbsolute);
			writer.WriteBoolean("IsAuto", value.IsAuto);
			writer.WriteBoolean("IsStar", value.IsStar);
			writer.WriteEndObject();
		}
	}
}
