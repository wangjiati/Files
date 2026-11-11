// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ImagePreviewViewModel : BasePreviewModel
	{
		private ImageSource? imageSource;
		public ImageSource? ImageSource
		{
			get => imageSource;
			private set => SetProperty(ref imageSource, value);
		}

		public ImagePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			using IRandomAccessStream stream = await PreviewFile.OpenAsync(FileAccessMode.Read);

			var decoder = await BitmapDecoder.CreateAsync(stream);
			bool landscape = decoder.PixelWidth >= decoder.PixelHeight;

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				BitmapImage bitmap = new() { DecodePixelType = DecodePixelType.Logical };
				if (landscape)
					bitmap.DecodePixelWidth = Math.Min(1024, (int)decoder.PixelWidth);
				else
					bitmap.DecodePixelHeight = Math.Min(1024, (int)decoder.PixelHeight);
				stream.Seek(0);
				await bitmap.SetSourceAsync(stream);
				ImageSource = bitmap;
			});

			return [];
		}
	}
}
