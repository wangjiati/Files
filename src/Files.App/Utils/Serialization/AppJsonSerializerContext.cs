// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Serialization
{
	[JsonSourceGenerationOptions(Converters = [typeof(GridLengthJsonConverter)])]
	[JsonSerializable(typeof(PaneNavigationArguments))]
	[JsonSerializable(typeof(TaggedFile[]))]
	[JsonSerializable(typeof(List<TaggedFile>))]
	[JsonSerializable(typeof(LayoutPreferencesDatabaseItem[]))]
	[JsonSerializable(typeof(List<LayoutPreferencesDatabaseItem>))]
	[JsonSerializable(typeof(LayoutPreferencesItem))]
	[JsonSerializable(typeof(List<ViewModels.Properties.FileProperty>))]
	internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
	{
	}
}
