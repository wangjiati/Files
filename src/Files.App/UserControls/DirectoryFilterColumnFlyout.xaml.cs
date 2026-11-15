// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

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
			TitleText.Text = viewModel.Title;
			ColumnEditor.Initialize(viewModel);
		}

		private void Apply_Click(object sender, RoutedEventArgs e)
		{
			if (ColumnEditor.Commit())
				ApplyRequested?.Invoke(this, EventArgs.Empty);
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
			=> ClearRequested?.Invoke(this, EventArgs.Empty);

		private void ClearAll_Click(object sender, RoutedEventArgs e)
			=> ClearAllRequested?.Invoke(this, EventArgs.Empty);
	}
}
