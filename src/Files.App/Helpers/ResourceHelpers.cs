// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Markup;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Files.App.Helpers
{
	[MarkupExtensionReturnType(ReturnType = typeof(string))]
	public sealed partial class ResourceString : MarkupExtension
	{
		private static readonly ResourceManager resourceManager = new();
		private static readonly ResourceContext resourceContext = resourceManager.CreateResourceContext();
		private static readonly ResourceMap resourcesTree = resourceManager.MainResourceMap.TryGetSubtree("Resources");

		public string Name { get; set; } = string.Empty;

		protected override object ProvideValue() => GetResourceString(Name);

		internal static string GetResourceString(string name)
			=> resourcesTree?.TryGetValue(name, resourceContext)?.ValueAsString ?? string.Empty;
	}
}
