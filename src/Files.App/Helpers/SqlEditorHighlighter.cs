// Copyright (c) Files Community
// Licensed under the MIT License.

using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI;

namespace Files.App.Helpers
{
	/// <summary>
	/// Applies ColorCode's SQL scopes to an editable RichEditBox without replacing its text.
	/// </summary>
	internal sealed class SqlEditorHighlighter : CodeColorizerBase
	{
		private readonly RichEditBox editor;
		private readonly Color defaultColor;
		private int absoluteOffset;

		public SqlEditorHighlighter(RichEditBox editor, bool isDark)
			: base(CreateStyles(isDark), null)
		{
			this.editor = editor;

			var plainText = Styles.Contains("Plain Text") ? Styles["Plain Text"].Foreground : null;
			defaultColor = TryParseColor(plainText) ?? (isDark ? Colors.White : Colors.Black);
		}

		public void Highlight(string text)
		{
			var selection = editor.Document.Selection;
			var selectionStart = selection.StartPosition;
			var selectionEnd = selection.EndPosition;

			var documentRange = editor.Document.GetRange(0, text.Length);
			documentRange.CharacterFormat.ForegroundColor = defaultColor;
			documentRange.CharacterFormat.Bold = FormatEffect.Off;
			documentRange.CharacterFormat.Italic = FormatEffect.Off;

			absoluteOffset = 0;
			languageParser.Parse(text, Languages.Sql, Write);

			selection.SetRange(selectionStart, selectionEnd);
		}

		protected override void Write(string parsedSourceCode, IList<Scope> scopes)
		{
			foreach (var scope in scopes)
				ApplyScope(scope);

			absoluteOffset += parsedSourceCode.Length;
		}

		private void ApplyScope(Scope scope)
		{
			if (Styles.Contains(scope.Name))
			{
				var style = Styles[scope.Name];
				var start = absoluteOffset + scope.Index;
				var end = start + scope.Length;
				if (end > start && start >= 0)
				{
					var range = editor.Document.GetRange(start, end);
					if (TryParseColor(style.Foreground) is { } foreground)
						range.CharacterFormat.ForegroundColor = foreground;

					if (style.Bold)
						range.CharacterFormat.Bold = FormatEffect.On;

					if (style.Italic)
						range.CharacterFormat.Italic = FormatEffect.On;
				}
			}

			foreach (var child in scope.Children)
				ApplyScope(child);
		}

		private static StyleDictionary CreateStyles(bool isDark)
		{
			var styles = isDark ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight;

			if (styles.Contains("Keyword"))
				styles["Keyword"].Bold = true;

			if (styles.Contains("SQL System Function"))
			{
				var function = styles["SQL System Function"];
				function.Foreground = isDark ? "#FFDCDCAA" : "#FF795E26";
				function.Bold = false;
			}

			return styles;
		}

		private static Color? TryParseColor(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var hex = value.TrimStart('#');
			if (hex.Length == 6)
				hex = "FF" + hex;

			if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
				return null;

			return Color.FromArgb(
				(byte)(argb >> 24),
				(byte)(argb >> 16),
				(byte)(argb >> 8),
				(byte)argb);
		}
	}
}
