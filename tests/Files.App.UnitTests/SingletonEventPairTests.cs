// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.ComponentModel;

namespace Files.App.UnitTests
{
	/// <summary>
	/// Backs the M5 retention fix: MainPage must be able to detach from singleton event
	/// sources so a rebuilt page tree is not rooted by them, and re-attach idempotently
	/// when a cached page is loaded again.
	/// </summary>
	[TestClass]
	public sealed class SingletonEventPairTests
	{
		private sealed class Source : INotifyPropertyChanged
		{
			public event PropertyChangedEventHandler? PropertyChanged;

			public int HandlerCount => PropertyChanged?.GetInvocationList().Length ?? 0;

			public void Raise() => PropertyChanged?.Invoke(this, new(string.Empty));
		}

		private sealed class Subscriber
		{
			public int Notifications { get; private set; }

			public void Handler(object? sender, PropertyChangedEventArgs e) => Notifications++;
		}

		[TestMethod]
		public void SingletonEventPair_RepeatedAttachKeepsSingleSubscription()
		{
			var source = new Source();
			var subscriber = new Subscriber();

			// Loaded can fire again for a cached page; pairing must not stack handlers.
			SingletonEventPair.Attach(source, subscriber.Handler);
			SingletonEventPair.Attach(source, subscriber.Handler);

			Assert.AreEqual(1, source.HandlerCount);

			source.Raise();
			Assert.AreEqual(1, subscriber.Notifications);
		}

		[TestMethod]
		public void SingletonEventPair_DetachRemovesSubscriptionAndIsRepeatable()
		{
			var source = new Source();
			var subscriber = new Subscriber();
			SingletonEventPair.Attach(source, subscriber.Handler);

			// Unloaded and OnNavigatedFrom both detach; the second detach must be a no-op.
			SingletonEventPair.Detach(source, subscriber.Handler);
			SingletonEventPair.Detach(source, subscriber.Handler);

			Assert.AreEqual(0, source.HandlerCount);

			source.Raise();
			Assert.AreEqual(0, subscriber.Notifications);
		}

		[TestMethod]
		public void SingletonEventPair_DetachedSubscriberIsNoLongerRootedBySource()
		{
			var source = new Source();
			var weak = AttachAndDetach(source);

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Assert.IsFalse(weak.IsAlive, "A detached subscriber must not be rooted by the event source");
		}

		private static WeakReference AttachAndDetach(Source source)
		{
			var subscriber = new Subscriber();
			var weak = new WeakReference(subscriber);

			SingletonEventPair.Attach(source, subscriber.Handler);
			SingletonEventPair.Detach(source, subscriber.Handler);

			return weak;
		}
	}
}
