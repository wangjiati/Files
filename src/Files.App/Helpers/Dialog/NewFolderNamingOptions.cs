// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Extensions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Helpers
{
	internal sealed class NewFolderNamingOptions
	{
		private static readonly NewFolderNameComponent[] CanonicalComponents =
			[NewFolderNameComponent.Name, NewFolderNameComponent.Date, NewFolderNameComponent.Time, NewFolderNameComponent.YearMonth];

		private static readonly NamingPreset[] Presets =
		[
			new("Name", [NewFolderNameComponent.Name]),
			new("Date", [NewFolderNameComponent.Date]),
			new("Time", [NewFolderNameComponent.Time]),
			new("YearMonth", [NewFolderNameComponent.YearMonth]),
			new("NameDate", [NewFolderNameComponent.Name, NewFolderNameComponent.Date]),
			new("NameTime", [NewFolderNameComponent.Name, NewFolderNameComponent.Time]),
			new("DateTime", [NewFolderNameComponent.Date, NewFolderNameComponent.Time]),
			new("NameDateTime", [NewFolderNameComponent.Name, NewFolderNameComponent.Date, NewFolderNameComponent.Time]),
		];

		private readonly TextBox finalNameTextBox;
		private readonly IFoldersSettingsService settingsService;
		private readonly DateTimeOffset timestamp;
		private readonly string baseName;
		private readonly string separator;
		private readonly HashSet<NewFolderNameComponent> initialComponents;
		private List<ComponentRow> rows;
		private ComboBox presetComboBox = null!;
		private ComboBox separatorComboBox = null!;
		private ComboBoxItem customPresetItem = null!;
		private StackPanel componentPanel = null!;
		private InfoBar noComponentInfo = null!;
		private bool isUpdating;

		public NewFolderNamingOptions(
			TextBox finalNameTextBox,
			IFoldersSettingsService settingsService,
			DateTimeOffset timestamp,
			string baseName)
		{
			this.finalNameTextBox = finalNameTextBox;
			this.settingsService = settingsService;
			this.timestamp = timestamp;
			this.baseName = baseName;
			separator = NewFolderNameFormatter.NormalizeSeparator(settingsService.NewFolderNameSeparator);

			var selectedComponents = NewFolderNameFormatter.ParseComponents(settingsService.NewFolderNameComponents);
			initialComponents = selectedComponents.ToHashSet();
			rows = selectedComponents
				.Concat(CanonicalComponents.Except(selectedComponents))
				.Select(component => new ComponentRow(component))
				.ToList();
		}

		public FrameworkElement CreateView()
		{
			isUpdating = true;

			presetComboBox = new()
			{
				Header = Strings.NewFolderNamingScheme.GetLocalizedResource(),
				HorizontalAlignment = HorizontalAlignment.Stretch,
			};
			AutomationProperties.SetAutomationId(presetComboBox, "CreateItemDialogNamingSchemeComboBox");
			foreach (var preset in Presets)
			{
				var item = new ComboBoxItem()
				{
					Content = string.Join(" + ", preset.Components.Select(GetComponentLabel)),
					Tag = preset,
				};
				AutomationProperties.SetAutomationId(item, $"CreateItemDialogNamingScheme{preset.Id}");
				presetComboBox.Items.Add(item);
			}
			customPresetItem = new() { Content = Strings.NewFolderNamingCustom.GetLocalizedResource() };
			presetComboBox.Items.Add(customPresetItem);
			presetComboBox.SelectionChanged += (s, e) =>
			{
				if (!isUpdating && (s as ComboBox)?.SelectedItem is ComboBoxItem { Tag: NamingPreset preset })
					ApplyPreset(preset);
			};

			componentPanel = new() { Spacing = 4d };
			foreach (var row in rows)
				InitializeRow(row);

			noComponentInfo = new()
			{
				Severity = InfoBarSeverity.Informational,
				IsClosable = false,
				IsOpen = false,
				Message = Strings.NewFolderNamingSelectComponent.GetLocalizedResource(),
			};

			separatorComboBox = new()
			{
				Header = Strings.NewFolderNamingSeparator.GetLocalizedResource(),
				HorizontalAlignment = HorizontalAlignment.Stretch,
			};
			AutomationProperties.SetAutomationId(separatorComboBox, "CreateItemDialogNamingSeparatorComboBox");
			AddSeparatorItem(Strings.NewFolderNamingSeparatorUnderscore.GetLocalizedResource(), "_", "Underscore");
			AddSeparatorItem(Strings.NewFolderNamingSeparatorHyphen.GetLocalizedResource(), "-", "Hyphen");
			AddSeparatorItem(Strings.NewFolderNamingSeparatorSpace.GetLocalizedResource(), " ", "Space");
			AddSeparatorItem(Strings.NewFolderNamingSeparatorNone.GetLocalizedResource(), "", "None");
			separatorComboBox.SelectedItem = separatorComboBox.Items
				.OfType<ComboBoxItem>()
				.First(item => Equals(item.Tag, separator));
			separatorComboBox.SelectionChanged += (s, e) =>
			{
				if (!isUpdating)
					UpdateGeneratedName();
			};

			RebuildRows();
			UpdatePresetSelection();
			isUpdating = false;

			return new StackPanel()
			{
				Spacing = 8d,
				Children =
				{
					presetComboBox,
					new TextBlock() { Text = Strings.NewFolderNamingComponents.GetLocalizedResource() },
					componentPanel,
					noComponentInfo,
					separatorComboBox,
				},
			};
		}

		public void UpdateGeneratedName()
		{
			var components = GetSelectedComponents();
			finalNameTextBox.Text = NewFolderNameFormatter.Generate(baseName, timestamp, components, GetSelectedSeparator());
			noComponentInfo.IsOpen = components.Count == 0;
		}

		public void Save()
		{
			settingsService.NewFolderNameComponents = GetSelectedComponents().Select(component => component.ToString()).ToList();
			settingsService.NewFolderNameSeparator = GetSelectedSeparator();
		}

		private void AddSeparatorItem(string label, string value, string id)
		{
			var item = new ComboBoxItem() { Content = label, Tag = value };
			AutomationProperties.SetAutomationId(item, $"CreateItemDialogNamingSeparator{id}");
			separatorComboBox.Items.Add(item);
		}

		private void InitializeRow(ComponentRow row)
		{
			var label = GetComponentLabel(row.Component);
			row.CheckBox = new()
			{
				Content = label,
				IsChecked = IsComponentInitiallySelected(row.Component),
				MinHeight = 32d,
			};
			AutomationProperties.SetAutomationId(row.CheckBox, $"CreateItemDialogComponent{row.Component}CheckBox");
			row.CheckBox.Checked += (s, e) => OnComponentChanged();
			row.CheckBox.Unchecked += (s, e) => OnComponentChanged();

			row.UpButton = CreateMoveButton(row, true, label);
			row.DownButton = CreateMoveButton(row, false, label);
			row.Root = new()
			{
				ColumnDefinitions =
				{
					new() { Width = new(1d, GridUnitType.Star) },
					new() { Width = GridLength.Auto },
					new() { Width = GridLength.Auto },
				},
				Children =
				{
					row.CheckBox,
					row.UpButton,
					row.DownButton,
				},
			};
			Grid.SetColumn(row.UpButton, 1);
			Grid.SetColumn(row.DownButton, 2);
		}

		private Button CreateMoveButton(ComponentRow row, bool moveUp, string label)
		{
			var button = new Button()
			{
				Content = new FontIcon() { Glyph = moveUp ? "\uE70E" : "\uE70D" },
				MinWidth = 32d,
				MinHeight = 32d,
				Margin = new(4d, 0d, 0d, 0d),
			};
			var automationName = string.Format(
				(moveUp ? Strings.NewFolderNamingMoveUp : Strings.NewFolderNamingMoveDown).GetLocalizedResource(),
				label);
			AutomationProperties.SetName(button, automationName);
			AutomationProperties.SetAutomationId(button, $"CreateItemDialogMove{row.Component}{(moveUp ? "Up" : "Down")}Button");
			button.Click += (s, e) => MoveRow(row, moveUp ? -1 : 1);
			return button;
		}

		private bool IsComponentInitiallySelected(NewFolderNameComponent component)
			=> initialComponents.Contains(component);

		private void OnComponentChanged()
		{
			if (isUpdating)
				return;

			UpdatePresetSelection();
			UpdateGeneratedName();
		}

		private void ApplyPreset(NamingPreset preset)
		{
			isUpdating = true;
			rows = preset.Components
				.Select(component => rows.Single(row => row.Component == component))
				.Concat(rows.Where(row => !preset.Components.Contains(row.Component)).OrderBy(row => Array.IndexOf(CanonicalComponents, row.Component)))
				.ToList();
			foreach (var row in rows)
				row.CheckBox.IsChecked = preset.Components.Contains(row.Component);
			RebuildRows();
			isUpdating = false;

			UpdateGeneratedName();
		}

		private void MoveRow(ComponentRow row, int offset)
		{
			var index = rows.IndexOf(row);
			var targetIndex = index + offset;
			if (targetIndex < 0 || targetIndex >= rows.Count)
				return;

			(rows[index], rows[targetIndex]) = (rows[targetIndex], rows[index]);
			RebuildRows();
			UpdatePresetSelection();
			UpdateGeneratedName();
			RestoreMoveFocus(row, offset);
		}

		private static void RestoreMoveFocus(ComponentRow row, int offset)
		{
			var preferredButton = offset < 0 ? row.UpButton : row.DownButton;
			var fallbackButton = offset < 0 ? row.DownButton : row.UpButton;
			Control focusTarget = preferredButton.IsEnabled
				? preferredButton
				: fallbackButton.IsEnabled ? fallbackButton : row.CheckBox;
			focusTarget.Focus(FocusState.Programmatic);
		}

		private void RebuildRows()
		{
			componentPanel.Children.Clear();
			for (var index = 0; index < rows.Count; index++)
			{
				var row = rows[index];
				row.UpButton.IsEnabled = index > 0;
				row.DownButton.IsEnabled = index < rows.Count - 1;
				componentPanel.Children.Add(row.Root);
			}
		}

		private void UpdatePresetSelection()
		{
			var components = GetSelectedComponents();
			var matchingPreset = Presets.FirstOrDefault(preset => preset.Components.SequenceEqual(components));
			isUpdating = true;
			presetComboBox.SelectedItem = matchingPreset is null
				? customPresetItem
				: presetComboBox.Items.OfType<ComboBoxItem>().First(item => ReferenceEquals(item.Tag, matchingPreset));
			isUpdating = false;
		}

		private List<NewFolderNameComponent> GetSelectedComponents()
			=> rows.Where(row => row.CheckBox.IsChecked == true).Select(row => row.Component).ToList();

		private string GetSelectedSeparator()
			=> (separatorComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "_";

		private static string GetComponentLabel(NewFolderNameComponent component)
			=> component switch
			{
				NewFolderNameComponent.Name => Strings.NewFolderNamingComponentName.GetLocalizedResource(),
				NewFolderNameComponent.Date => Strings.NewFolderNamingComponentDate.GetLocalizedResource(),
				NewFolderNameComponent.Time => Strings.NewFolderNamingComponentTime.GetLocalizedResource(),
				NewFolderNameComponent.YearMonth => Strings.NewFolderNamingComponentYearMonth.GetLocalizedResource(),
				_ => throw new ArgumentOutOfRangeException(nameof(component)),
			};

		private sealed record NamingPreset(string Id, NewFolderNameComponent[] Components);

		private sealed class ComponentRow(NewFolderNameComponent component)
		{
			public NewFolderNameComponent Component { get; } = component;
			public Grid Root { get; set; } = null!;
			public CheckBox CheckBox { get; set; } = null!;
			public Button UpButton { get; set; } = null!;
			public Button DownButton { get; set; } = null!;
		}
	}
}
