// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenameEngineTests
	{
		private static readonly DateTimeOffset CreatedUtc = new(2026, 11, 1, 9, 5, 7, TimeSpan.Zero);
		private static readonly DateTimeOffset ModifiedUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

		private static RenameTarget Target(string name, string parentFolderName = "夜景")
		{
			return new RenameTarget(
				FullPath: $"C:\\照片\\{parentFolderName}\\{name}",
				Name: name,
				IsFolder: false,
				CreatedUtc: CreatedUtc,
				ModifiedUtc: ModifiedUtc,
				ParentFolderName: parentFolderName);
		}

		[TestMethod]
		public void RegexMode_WithCaptureGroup_RewritesName()
		{
			var rule = new RenameRule(
				Template: RenameTemplate.FindReplace,
				Find: "IMG_(\\d+)",
				ReplaceWith: "京都_$1",
				Mode: RenameMode.Regex,
				ApplyTo: RenameApplyTo.NameOnly,
				MatchAllOccurrences: true);

			var outcomes = new RenameEngine().Run([Target("IMG_2049.jpg"), Target("DSC_0001.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual(RenameError.None, outcomes[0].Error);
			Assert.AreEqual("京都_2049.jpg", outcomes[0].NewName);
			Assert.AreEqual(RenameOutcomeKind.Unchanged, outcomes[1].Kind);
			Assert.AreEqual("DSC_0001.jpg", outcomes[1].NewName);
		}

		[TestMethod]
		public void TextMode_CaseInsensitive_MatchesDifferentCase()
		{
			var rule = new RenameRule(Find: "IMG_", ReplaceWith: "photo_", Mode: RenameMode.Text, CaseSensitive: false);

			var outcomes = new RenameEngine().Run([Target("img_2053.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("photo_2053.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void TextMode_CaseSensitive_DoesNotMatchDifferentCase()
		{
			var rule = new RenameRule(Find: "IMG_", ReplaceWith: "photo_", Mode: RenameMode.Text, CaseSensitive: true);

			var outcomes = new RenameEngine().Run([Target("img_2053.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Unchanged, outcomes[0].Kind);
			Assert.AreEqual("img_2053.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void WildcardMode_StarInReplacement_MapsToCapturedContent()
		{
			var rule = new RenameRule(
				Find: "IMG_*",
				ReplaceWith: "照片_*",
				Mode: RenameMode.Wildcard,
				ApplyTo: RenameApplyTo.NameOnly);

			var outcomes = new RenameEngine().Run([Target("IMG_2049.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("照片_2049.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void TextMode_MatchAllFalse_ReplacesOnlyFirstOccurrence()
		{
			var rule = new RenameRule(Find: "power", ReplaceWith: "super", Mode: RenameMode.Text, MatchAllOccurrences: false);

			var outcomes = new RenameEngine().Run([Target("powertoys-powerrename")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("supertoys-powerrename", outcomes[0].NewName);
		}

		[TestMethod]
		public void Prepend_AddsPrefixToNameOnly_AndSkipsItemsAlreadyPrefixed()
		{
			var rule = new RenameRule(
				Template: RenameTemplate.Prepend,
				Prefix: "京都之旅_",
				ApplyTo: RenameApplyTo.NameOnly,
				SkipItemsWithPrefix: true);

			var outcomes = new RenameEngine().Run([Target("img_2053.jpg"), Target("京都之旅_old.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("京都之旅_img_2053.jpg", outcomes[0].NewName);
			Assert.AreEqual(RenameOutcomeKind.Unchanged, outcomes[1].Kind);
		}

		[TestMethod]
		public void Append_InsertBeforeExtension_InsertsBeforeDot()
		{
			var rule = new RenameRule(Template: RenameTemplate.Append, Suffix: "_备份", InsertBeforeExtension: true);

			var outcomes = new RenameEngine().Run([Target("img.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("img_备份.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void Append_AtEnd_AppendsAfterExtension()
		{
			var rule = new RenameRule(Template: RenameTemplate.Append, Suffix: "_备份", InsertBeforeExtension: false);

			var outcomes = new RenameEngine().Run([Target("img.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("img.jpg_备份", outcomes[0].NewName);
		}

		[TestMethod]
		public void ApplyTo_ExtensionOnly_RewritesExtension()
		{
			var rule = new RenameRule(Find: "txt", ReplaceWith: "NewExt", Mode: RenameMode.Text, ApplyTo: RenameApplyTo.ExtensionOnly);

			var outcomes = new RenameEngine().Run([Target("txt.txt")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("txt.NewExt", outcomes[0].NewName);
		}

		[TestMethod]
		public void ApplyTo_NameAndExtension_RewritesWholeName()
		{
			var rule = new RenameRule(Find: "txt.txt", ReplaceWith: "NewName.NewExt", Mode: RenameMode.Text, ApplyTo: RenameApplyTo.NameAndExtension);

			var outcomes = new RenameEngine().Run([Target("txt.txt")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("NewName.NewExt", outcomes[0].NewName);
		}

		[TestMethod]
		public void CaseConversion_EveryWord_KeepsExtensionUntouched()
		{
			var rule = new RenameRule(
				Find: "hello world",
				ReplaceWith: "hello world",
				Mode: RenameMode.Text,
				CaseConversion: RenameCaseConversion.EveryWord);

			var outcomes = new RenameEngine().Run([Target("hello world.txt")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("Hello World.txt", outcomes[0].NewName);
		}

		[TestMethod]
		public void TextMode_VariableInReplacement_ExpandsFolderName()
		{
			var rule = new RenameRule(Find: "IMG_", ReplaceWith: "(${folder})_", Mode: RenameMode.Text);

			var outcomes = new RenameEngine().Run([Target("IMG_2049.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("(夜景)_2049.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void RegexMode_VariableInReplacement_ExpandsFolderNameAndKeepsGroupReferences()
		{
			var rule = new RenameRule(Find: "IMG_(\\d+)", ReplaceWith: "京都_${folder}_$1", Mode: RenameMode.Regex);

			var outcomes = new RenameEngine().Run([Target("IMG_2049.jpg"), Target("IMG_2050.jpg", "夜景$a")], rule);

			Assert.AreEqual(RenameOutcomeKind.Changed, outcomes[0].Kind);
			Assert.AreEqual("京都_夜景_2049.jpg", outcomes[0].NewName);
			Assert.AreEqual("京都_夜景$a_2050.jpg", outcomes[1].NewName);
		}

		[TestMethod]
		public void NewNameEqualsOriginal_IsUnchanged()
		{
			var rule = new RenameRule(Find: "IMG", ReplaceWith: "IMG", Mode: RenameMode.Text);

			var outcomes = new RenameEngine().Run([Target("IMG_2049.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.Unchanged, outcomes[0].Kind);
			Assert.AreEqual(RenameError.None, outcomes[0].Error);
			Assert.AreEqual("IMG_2049.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void Counter_AdvancesOnlyForChangedRows()
		{
			var rule = new RenameRule(Find: "img", ReplaceWith: "photo_${}", Mode: RenameMode.Text);

			var outcomes = new RenameEngine().Run(
				[Target("img.jpg", "夜景"), Target("other.jpg", "别册"), Target("img.jpg", "备用")],
				rule);

			Assert.AreEqual("photo_0.jpg", outcomes[0].NewName);
			Assert.AreEqual(RenameOutcomeKind.Unchanged, outcomes[1].Kind);
			Assert.AreEqual("photo_1.jpg", outcomes[2].NewName);
		}

		[TestMethod]
		public void InvalidRegex_ProducesBadPatternError()
		{
			var rule = new RenameRule(Find: "(", Mode: RenameMode.Regex);

			var outcomes = new RenameEngine().Run([Target("a.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.RuleError, outcomes[0].Kind);
			Assert.AreEqual(RenameError.BadPattern, outcomes[0].Error);
			Assert.AreEqual("a.jpg", outcomes[0].NewName);
		}

		[TestMethod]
		public void CatastrophicRegex_ProducesTimeoutError()
		{
			var rule = new RenameRule(Find: "(a+)+$", ReplaceWith: "x", Mode: RenameMode.Regex);

			var outcomes = new RenameEngine().Run([Target(new string('a', 30) + "b")], rule);

			Assert.AreEqual(RenameOutcomeKind.RuleError, outcomes[0].Kind);
			Assert.AreEqual(RenameError.RegexTimeout, outcomes[0].Error);
		}

		[TestMethod]
		public void InvalidNewName_ProducesInvalidNameError()
		{
			var rule = new RenameRule(Find: "img", ReplaceWith: "京都_?", Mode: RenameMode.Text);

			var outcomes = new RenameEngine().Run([Target("img.jpg")], rule);

			Assert.AreEqual(RenameOutcomeKind.RuleError, outcomes[0].Kind);
			Assert.AreEqual(RenameError.InvalidName, outcomes[0].Error);
			Assert.AreEqual("京都_?.jpg", outcomes[0].NewName);
		}
	}
}
