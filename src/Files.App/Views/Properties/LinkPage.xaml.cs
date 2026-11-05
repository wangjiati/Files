// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;

namespace Files.App.Views.Properties
{
	public sealed partial class LinkPage : BasePropertiesPage
	{
		public LinkPage()
		{
			InitializeComponent();
		}

		public override Task<bool> SaveChangesAsync()
			=> Task.FromResult(true);

		public override void Dispose()
		{
		}
	}
}
