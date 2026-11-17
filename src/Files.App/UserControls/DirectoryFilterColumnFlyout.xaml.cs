// Copyright (c) Files Community
// Licensed under the MIT License.

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

		private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			// The editor's text boxes handle Enter themselves; this catches focus elsewhere in the form.
			if (e.Handled || e.Key is not VirtualKey.Enter || e.OriginalSource is not TextBox)
				return;

			e.Handled = true;
			if (ColumnEditor.Commit())
				ApplyRequested?.Invoke(this, EventArgs.Empty);
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
			=> ClearRequested?.Invoke(this, EventArgs.Empty);

		private void ClearAll_Click(object sender, RoutedEventArgs e)
			=> ClearAllRequested?.Invoke(this, EventArgs.Empty);
	}
}
