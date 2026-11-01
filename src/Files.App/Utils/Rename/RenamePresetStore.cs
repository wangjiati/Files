// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	[JsonSourceGenerationOptions(WriteIndented = true)]
	[JsonSerializable(typeof(List<RenamePreset>))]
	[JsonSerializable(typeof(IReadOnlyList<RenamePreset>))]
	[JsonSerializable(typeof(RenamePreset))]
	[JsonSerializable(typeof(RenameRule))]
	[JsonSerializable(typeof(RenameTemplate))]
	[JsonSerializable(typeof(RenameMode))]
	[JsonSerializable(typeof(RenameApplyTo))]
	[JsonSerializable(typeof(RenameCaseConversion))]
	[JsonSerializable(typeof(FileDateSource))]
	[JsonSerializable(typeof(RenameScopeOption))]
	internal sealed partial class RenamePresetsJsonContext : JsonSerializerContext
	{
	}

	/// <summary>
	/// Loads and saves rename presets as a JSON file on disk.
	/// </summary>
	public sealed class RenamePresetStore
	{
		private readonly string _filePath;

		public RenamePresetStore(string filePath)
		{
			ArgumentNullException.ThrowIfNull(filePath);

			_filePath = filePath;
		}

		/// <summary>
		/// Reads the presets from the file, returning an empty list when the file is missing or empty.
		/// </summary>
		public async Task<IReadOnlyList<RenamePreset>> LoadAsync()
		{
			if (!SystemIO.File.Exists(_filePath))
				return [];

			await using var stream = SystemIO.File.OpenRead(_filePath);
			if (stream.Length == 0)
				return [];

			var presets = await JsonSerializer.DeserializeAsync(stream, RenamePresetsJsonContext.Default.ListRenamePreset);

			return presets ?? [];
		}

		/// <summary>
		/// Writes the presets to the file, replacing any previous content.
		/// </summary>
		public async Task SaveAsync(IReadOnlyList<RenamePreset> presets)
		{
			ArgumentNullException.ThrowIfNull(presets);

			await using var stream = SystemIO.File.Create(_filePath);
			await JsonSerializer.SerializeAsync(stream, presets, RenamePresetsJsonContext.Default.IReadOnlyListRenamePreset);
		}
	}
}
