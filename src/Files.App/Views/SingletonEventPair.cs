// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;

namespace Files.App.Views
{
	/// <summary>
	/// Idempotent -=/+= pairing for <see cref="MainPage"/>'s subscriptions to singleton event
	/// sources. The root frame can rebuild the page while its singletons live on, so the page
	/// must fully detach on unload and be able to re-attach when a cached instance loads again.
	/// </summary>
	public static class SingletonEventPair
	{
		public static void Attach(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
		{
			source.PropertyChanged -= handler;
			source.PropertyChanged += handler;
		}

		public static void Detach(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
		{
			source.PropertyChanged -= handler;
		}
	}
}
