// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class BatchRenameAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.BatchRenameActionLabel.GetLocalizedResource();

		public string Description
			=> Strings.BatchRenameActionDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.FileSystem;

		public RichGlyph Glyph
			=> new(baseGlyph: "\uE8AC");

		public bool IsExecutable =>
			context.ShellPage is not null &&
			IsPageTypeValid() &&
			context.ShellPage.SlimContentPage is not null;

		public BatchRenameAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			if (context.ShellPage is { } shellPage)
				BatchRenameOverlayPresenter.Instance?.Open(shellPage, context.SelectedItems);

			return Task.CompletedTask;
		}

		private bool IsPageTypeValid()
		{
			return
				context.PageType != ContentPageTypes.None &&
				context.PageType != ContentPageTypes.Home &&
				context.PageType != ContentPageTypes.RecycleBin &&
				context.PageType != ContentPageTypes.ZipFolder &&
				context.PageType != ContentPageTypes.ReleaseNotes &&
				context.PageType != ContentPageTypes.Settings;
		}

		private void Context_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.ShellPage):
				case nameof(IContentPageContext.PageType):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
