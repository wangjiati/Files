// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenamePresetStoreTests
	{
		[TestMethod]
		public async Task SaveAsync_LoadAsync_PreservesEveryField()
		{
			var path = Path.Combine(Path.GetTempPath(), $"rename_presets_test_{Guid.NewGuid():N}.json");
			try
			{
				var presets = new List<RenamePreset>
				{
					new(
						"递归正则改名",
						new RenameRule(
							Template: RenameTemplate.Append,
							Find: "IMG_",
							ReplaceWith: "照片",
							Mode: RenameMode.Regex,
							ApplyTo: RenameApplyTo.NameAndExtension,
							MatchAllOccurrences: false,
							CaseSensitive: true,
							Prefix: "预",
							SkipItemsWithPrefix: false,
							Suffix: "_终",
							InsertBeforeExtension: false,
							CaseConversion: RenameCaseConversion.EveryWord,
							DateSource: FileDateSource.Modified),
						RenameScopeOption.CurrentFolderWithSubfolders,
						Depth: 3,
						IncludeFiles: false,
						IncludeFolders: true),
					new("默认替换", new RenameRule(), RenameScopeOption.SelectedItems, 0, true, false),
				};
				var store = new RenamePresetStore(path);

				await store.SaveAsync(presets);
				var loaded = await store.LoadAsync();

				Assert.AreEqual(2, loaded.Count);
				AssertPresetFieldsAreEqual(presets[0], loaded[0]);
				AssertPresetFieldsAreEqual(presets[1], loaded[1]);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public async Task LoadAsync_EmptyFile_ReturnsEmptyList()
		{
			var path = Path.Combine(Path.GetTempPath(), $"rename_presets_test_{Guid.NewGuid():N}.json");
			try
			{
				File.WriteAllText(path, string.Empty);
				var store = new RenamePresetStore(path);

				var loaded = await store.LoadAsync();

				Assert.AreEqual(0, loaded.Count);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public async Task LoadAsync_MissingFile_ReturnsEmptyList()
		{
			var path = Path.Combine(Path.GetTempPath(), $"rename_presets_test_{Guid.NewGuid():N}.json");

			var loaded = await new RenamePresetStore(path).LoadAsync();

			Assert.AreEqual(0, loaded.Count);
		}

		private static void AssertPresetFieldsAreEqual(RenamePreset expected, RenamePreset actual)
		{
			Assert.AreEqual(expected.Name, actual.Name, nameof(RenamePreset.Name));
			AssertRuleFieldsAreEqual(expected.Rule, actual.Rule);
			Assert.AreEqual(expected.Scope, actual.Scope, nameof(RenamePreset.Scope));
			Assert.AreEqual(expected.Depth, actual.Depth, nameof(RenamePreset.Depth));
			Assert.AreEqual(expected.IncludeFiles, actual.IncludeFiles, nameof(RenamePreset.IncludeFiles));
			Assert.AreEqual(expected.IncludeFolders, actual.IncludeFolders, nameof(RenamePreset.IncludeFolders));
		}

		private static void AssertRuleFieldsAreEqual(RenameRule expected, RenameRule actual)
		{
			Assert.AreEqual(expected.Template, actual.Template, nameof(RenameRule.Template));
			Assert.AreEqual(expected.Find, actual.Find, nameof(RenameRule.Find));
			Assert.AreEqual(expected.ReplaceWith, actual.ReplaceWith, nameof(RenameRule.ReplaceWith));
			Assert.AreEqual(expected.Mode, actual.Mode, nameof(RenameRule.Mode));
			Assert.AreEqual(expected.ApplyTo, actual.ApplyTo, nameof(RenameRule.ApplyTo));
			Assert.AreEqual(expected.MatchAllOccurrences, actual.MatchAllOccurrences, nameof(RenameRule.MatchAllOccurrences));
			Assert.AreEqual(expected.CaseSensitive, actual.CaseSensitive, nameof(RenameRule.CaseSensitive));
			Assert.AreEqual(expected.Prefix, actual.Prefix, nameof(RenameRule.Prefix));
			Assert.AreEqual(expected.SkipItemsWithPrefix, actual.SkipItemsWithPrefix, nameof(RenameRule.SkipItemsWithPrefix));
			Assert.AreEqual(expected.Suffix, actual.Suffix, nameof(RenameRule.Suffix));
			Assert.AreEqual(expected.InsertBeforeExtension, actual.InsertBeforeExtension, nameof(RenameRule.InsertBeforeExtension));
			Assert.AreEqual(expected.CaseConversion, actual.CaseConversion, nameof(RenameRule.CaseConversion));
			Assert.AreEqual(expected.DateSource, actual.DateSource, nameof(RenameRule.DateSource));
		}
	}
}
