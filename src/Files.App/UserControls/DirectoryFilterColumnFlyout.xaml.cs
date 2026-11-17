// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace Files.App.UserControls
{
	/// <summary>
	/// Popup column filter. Hosts the same inline editor as the top bar; applying adds the
	/// condition to the bar so the query language and the condition list stay unified.
	/// </summary>
	public sealed partial class DirectoryFilterColumnFlyout : UserControl
	{
		public DirectoryFilterColumnFlyout()
		{
			InitializeComponent();
		}

		public event EventHandler? ApplyRequested;

		public event EventHandler? ClearRequested;

		public event EventHandler? ClearAllRequested;

		public DirectoryFilterViewModel? ViewModel { get; private set; }

		public void Initialize(DirectoryFilterViewModel viewModel)
		{
			ViewModel = viewModel;
			ColumnEditor.Initialize(viewModel);
		}

		private void Apply_Click(object sender, RoutedEventArgs e)
		{
			if (ColumnEditor.Commit())
				ApplyRequested?.Invoke(this, EventArgs.Empty);
		}

		private void Content_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			// Catch Enter before the TextBox consumes it.
			if (e.Handled || e.Key is not VirtualKey.Enter)
				return;

			var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject ?? e.OriginalSource as DependencyObject;
			if (!IsFilterTextInput(focused))
				return;

			e.Handled = true;
			if (ColumnEditor.Commit())
				ApplyRequested?.Invoke(this, EventArgs.Empty);
		}

		private static bool IsFilterTextInput(DependencyObject? element)
		{
			if (element is null)
				return false;

			if (DependencyObjectHelpers.FindParent<ComboBox>(element) is not null)
				return false;

			return element is TextBox || DependencyObjectHelpers.FindParent<TextBox>(element) is not null;
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
			=> ClearRequested?.Invoke(this, EventArgs.Empty);

		private void ClearAll_Click(object sender, RoutedEventArgs e)
			=> ClearAllRequested?.Invoke(this, EventArgs.Empty);
	}
}
