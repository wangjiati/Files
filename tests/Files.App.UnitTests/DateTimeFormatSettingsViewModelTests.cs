// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.Settings;
using Files.App.ViewModels.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DateTimeFormatSettingsViewModelTests
	{
		private TemporaryDirectory _directory = null!;

		[TestInitialize]
		public void Initialize()
		{
			_directory = new TemporaryDirectory();
			AppRuntime.Initialize(DistributionKind.Portable, _directory.Path);
			Directory.CreateDirectory(AppRuntime.Current.SettingsDirectory);
		}

		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
			_directory.Dispose();
		}

		[TestMethod]
		public void FormatChoices_MapPresetsInBothDirections()
		{
			Assert.AreEqual(DateTimeFormats.Application, DateTimeFormatSettingsViewModel.GetFormatForChoiceIndex(0));
			Assert.AreEqual(DateTimeFormats.System, DateTimeFormatSettingsViewModel.GetFormatForChoiceIndex(1));
			Assert.AreEqual(DateTimeFormats.Universal, DateTimeFormatSettingsViewModel.GetFormatForChoiceIndex(2));
			Assert.AreEqual(DateTimeFormats.Custom, DateTimeFormatSettingsViewModel.GetFormatForChoiceIndex(3));

			Assert.AreEqual(3, DateTimeFormatSettingsViewModel.GetFormatChoiceIndex(DateTimeFormats.Custom));
			Assert.AreEqual(2, DateTimeFormatSettingsViewModel.GetFormatChoiceIndex(DateTimeFormats.Universal));
		}

		[TestMethod]
		public void ColumnChoices_MapInheritAndPresets()
		{
			Assert.IsNull(DateTimeFormatSettingsViewModel.GetFormatForColumnChoiceIndex(0));
			Assert.AreEqual(DateTimeFormats.Application, DateTimeFormatSettingsViewModel.GetFormatForColumnChoiceIndex(1));
			Assert.AreEqual(DateTimeFormats.Custom, DateTimeFormatSettingsViewModel.GetFormatForColumnChoiceIndex(4));

			Assert.AreEqual(0, DateTimeFormatSettingsViewModel.GetColumnFormatChoiceIndex(null));
			Assert.AreEqual(3, DateTimeFormatSettingsViewModel.GetColumnFormatChoiceIndex(DateTimeFormats.Universal));
			Assert.AreEqual(4, DateTimeFormatSettingsViewModel.GetColumnFormatChoiceIndex(DateTimeFormats.Custom));
		}

		[TestMethod]
		public void GetPreview_FormatsCustomPatternWithMilliseconds()
		{
			var (_, viewModel) = CreateTarget();

			Assert.AreEqual(
				"2024-12-31 14:30:05.123",
				viewModel.GetPreview(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff"));
		}

		[TestMethod]
		public void GetPreview_ReturnsEmptyForInvalidPattern()
		{
			var (_, viewModel) = CreateTarget();

			Assert.AreEqual(string.Empty, viewModel.GetPreview(DateTimeFormats.Custom, "yyyy-MM-dd \\"));
		}

		[TestMethod]
		public void SelectedGlobalFormatIndex_SavesCustomPatternWhenItBecomesValid()
		{
			var (settings, viewModel) = CreateTarget();
			viewModel.SelectedGlobalFormatIndex = 3;

			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingPattern, viewModel.GlobalValidationError);
			Assert.AreEqual(DateTimeFormats.Application, settings.DateTimeFormat);

			viewModel.GlobalCustomPattern = "yyyy";

			Assert.AreEqual(DateTimeFormatRuleValidationError.None, viewModel.GlobalValidationError);
			Assert.AreEqual(DateTimeFormats.Custom, settings.DateTimeFormat);
			Assert.AreEqual("yyyy", settings.DateTimeFormatCustomPattern);
		}

		[TestMethod]
		public void SaveGlobalFormat_InvalidCustomPatternDoesNotPersist()
		{
			var (settings, viewModel) = CreateTarget();
			settings.DateTimeFormat = DateTimeFormats.System;
			settings.DateTimeFormatCustomPattern = null;

			var error = viewModel.SaveGlobalFormat(DateTimeFormats.Custom, "yyyy-MM-dd \\");

			Assert.AreEqual(DateTimeFormatRuleValidationError.InvalidPattern, error);
			Assert.AreEqual(DateTimeFormats.System, settings.DateTimeFormat);
			Assert.IsNull(settings.DateTimeFormatCustomPattern);
			Assert.AreEqual(DateTimeFormatRuleValidationError.InvalidPattern, viewModel.GlobalValidationError);
		}

		[TestMethod]
		public void SaveColumnRule_InvalidCustomPatternDoesNotPersist()
		{
			var (settings, viewModel) = CreateTarget();

			var error = viewModel.SaveColumnRule(DateTimeColumnKind.Modified, DateTimeFormats.Custom, "");

			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingPattern, error);
			Assert.AreEqual(0, settings.DateTimeColumnFormatRules.Count);
			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingPattern, viewModel.ModifiedRule.ValidationError);
		}

		[TestMethod]
		public void SaveColumnRule_PersistsPresetAndClearsRuleWhenInherited()
		{
			var (settings, viewModel) = CreateTarget();

			Assert.AreEqual(
				DateTimeFormatRuleValidationError.None,
				viewModel.SaveColumnRule(DateTimeColumnKind.Created, DateTimeFormats.Universal, null));

			var rule = settings.DateTimeColumnFormatRules.Single();
			Assert.AreEqual(DateTimeColumnKind.Created, rule.Column);
			Assert.AreEqual(DateTimeFormats.Universal, rule.Format);
			Assert.IsNull(rule.DirectoryPath);

			Assert.AreEqual(
				DateTimeFormatRuleValidationError.None,
				viewModel.SaveColumnRule(DateTimeColumnKind.Created, null, null));

			Assert.AreEqual(0, settings.DateTimeColumnFormatRules.Count);
		}

		[TestMethod]
		public void SaveDirectoryRule_InvalidCustomPatternDoesNotPersist()
		{
			var (settings, viewModel) = CreateTarget();

			var error = viewModel.SaveDirectoryRule(
				existingRule: null,
				directoryPath: @"C:\Work",
				column: DateTimeColumnKind.Modified,
				format: DateTimeFormats.Custom,
				customPattern: null,
				includeSubdirectories: false);

			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingPattern, error);
			Assert.AreEqual(0, settings.DateTimeFormatDirectoryRules.Count);
		}

		[TestMethod]
		public void SaveDirectoryRule_RequiresDirectoryPath()
		{
			var (settings, viewModel) = CreateTarget();

			var error = viewModel.SaveDirectoryRule(
				existingRule: null,
				directoryPath: " ",
				column: null,
				format: DateTimeFormats.Universal,
				customPattern: null,
				includeSubdirectories: false);

			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingDirectory, error);
			Assert.AreEqual(0, settings.DateTimeFormatDirectoryRules.Count);
		}

		[TestMethod]
		public void DeleteDirectoryRule_FallsBackToNextPriority()
		{
			var volumes = new FakeVolumePathProvider();
			var (settings, viewModel) = CreateTarget(volumes);
			settings.DateTimeFormat = DateTimeFormats.System;
			settings.DateTimeColumnFormatRules =
			[
				new DateTimeFormatRule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.Universal, null),
			];

			Assert.AreEqual(
				DateTimeFormatRuleValidationError.None,
				viewModel.SaveDirectoryRule(
					existingRule: null,
					directoryPath: @"C:\Work",
					column: DateTimeColumnKind.Modified,
					format: DateTimeFormats.Custom,
					customPattern: "yyyy",
					includeSubdirectories: false));

			var resolver = new DateTimeFormatResolver((IDateTimeFormatRuleSource)settings, volumes);
			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);

			viewModel.DeleteDirectoryRule(settings.DateTimeFormatDirectoryRules.Single());

			Assert.AreEqual(0, settings.DateTimeFormatDirectoryRules.Count);
			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void RuleDialog_InvalidCustomPatternBlocksSave()
		{
			var (settings, settingsViewModel) = CreateTarget();
			var dialog = new DateTimeFormatRuleDialogViewModel(
				settingsViewModel,
				existingRule: null,
				initialDirectoryPath: @"C:\Work",
				initialColumn: DateTimeColumnKind.Created)
			{
				SelectedFormatIndex = DateTimeFormatSettingsViewModel.GetFormatChoiceIndex(DateTimeFormats.Custom),
			};

			Assert.IsFalse(dialog.Save());
			Assert.IsTrue(dialog.HasValidationError);
			Assert.AreEqual(0, settings.DateTimeFormatDirectoryRules.Count);
		}

		[TestMethod]
		public void RuleDialog_SavePersistsScopeAndRecursiveFlag()
		{
			var (settings, settingsViewModel) = CreateTarget();
			var dialog = new DateTimeFormatRuleDialogViewModel(
				settingsViewModel,
				existingRule: null,
				initialDirectoryPath: @"D:\Docs",
				initialColumn: DateTimeColumnKind.Modified)
			{
				SelectedFormatIndex = DateTimeFormatSettingsViewModel.GetFormatChoiceIndex(DateTimeFormats.Universal),
				IncludeSubdirectories = true,
			};

			Assert.IsTrue(dialog.Save());

			var rule = settings.DateTimeFormatDirectoryRules.Single();
			Assert.AreEqual(DateTimeColumnKind.Modified, rule.Column);
			Assert.AreEqual(DateTimeFormats.Universal, rule.Format);
			Assert.IsTrue(rule.IncludeSubdirectories);
		}

		[TestMethod]
		public void RuleDialog_DeleteRemovesExistingRule()
		{
			var (settings, settingsViewModel) = CreateTarget();
			settingsViewModel.SaveDirectoryRule(null, @"C:\Work", DateTimeColumnKind.Created, DateTimeFormats.System, null, false);
			var dialog = new DateTimeFormatRuleDialogViewModel(settingsViewModel, settings.DateTimeFormatDirectoryRules.Single());

			Assert.IsTrue(dialog.Delete());
			Assert.AreEqual(0, settings.DateTimeFormatDirectoryRules.Count);
		}

		[TestMethod]
		public void SaveDirectoryRule_PersistsIncludeSubdirectories()
		{
			var volumes = new FakeVolumePathProvider();
			var (settings, viewModel) = CreateTarget(volumes);

			Assert.AreEqual(
				DateTimeFormatRuleValidationError.None,
				viewModel.SaveDirectoryRule(
					existingRule: null,
					directoryPath: @"C:\Work",
					column: null,
					format: DateTimeFormats.Universal,
					customPattern: null,
					includeSubdirectories: true));

			var reloaded = new GeneralSettingsService(new UserSettingsService().GetSharingContext(), volumes);

			Assert.AreEqual(1, reloaded.DateTimeFormatDirectoryRules.Count);
			Assert.IsTrue(reloaded.DateTimeFormatDirectoryRules[0].IncludeSubdirectories);
		}

		[TestMethod]
		public void SaveDirectoryRule_FormatOnlyEditKeepsVolumeIdentity()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "VOL1", @"\Projects");
			var (settings, viewModel) = CreateTarget(volumes);

			viewModel.SaveDirectoryRule(null, @"E:\Projects", DateTimeColumnKind.Modified, DateTimeFormats.Universal, null, false);
			var stored = settings.DateTimeFormatDirectoryRules.Single();
			Assert.AreEqual("VOL1", stored.VolumeId);

			var error = viewModel.SaveDirectoryRule(stored, stored.DirectoryPath!, DateTimeColumnKind.Modified, DateTimeFormats.System, null, false);

			Assert.AreEqual(DateTimeFormatRuleValidationError.None, error);
			stored = settings.DateTimeFormatDirectoryRules.Single();
			Assert.AreEqual("VOL1", stored.VolumeId);
			Assert.AreEqual(@"\Projects", stored.RelativePath);
			Assert.AreEqual(DateTimeFormats.System, stored.Format);
		}

		[TestMethod]
		public void SaveDirectoryRule_ChangingDirectoryClearsOldVolumeIdentity()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "VOL1", @"\Projects");
			var (settings, viewModel) = CreateTarget(volumes);

			viewModel.SaveDirectoryRule(null, @"E:\Projects", DateTimeColumnKind.Modified, DateTimeFormats.Universal, null, false);
			var stored = settings.DateTimeFormatDirectoryRules.Single();

			var error = viewModel.SaveDirectoryRule(stored, @"C:\Other", DateTimeColumnKind.Modified, DateTimeFormats.Universal, null, false);

			Assert.AreEqual(DateTimeFormatRuleValidationError.None, error);
			stored = settings.DateTimeFormatDirectoryRules.Single();
			Assert.AreEqual(@"C:\Other", stored.DirectoryPath);
			Assert.IsNull(stored.VolumeId);
			Assert.IsNull(stored.RelativePath);
		}

		private static (IGeneralSettingsService Settings, DateTimeFormatSettingsViewModel ViewModel) CreateTarget(
			IVolumePathProvider? volumes = null)
		{
			var settings = new GeneralSettingsService(new UserSettingsService().GetSharingContext(), volumes);
			var viewModel = new DateTimeFormatSettingsViewModel(settings, new DateTimeFormatterFactory(), volumes);

			return (settings, viewModel);
		}

		private sealed class FakeVolumePathProvider : IVolumePathProvider
		{
			private readonly System.Collections.Generic.Dictionary<string, (string VolumeId, string RelativePath)> _volumes = new(StringComparer.OrdinalIgnoreCase);

			public void AddVolume(string path, string volumeId, string relativePath)
				=> _volumes[PortableDirectoryKey.NormalizePath(path)] = (volumeId, relativePath);

			public bool TryGetVolumeIdentity(string directoryPath, out string volumeId, out string relativePath)
			{
				if (_volumes.TryGetValue(PortableDirectoryKey.NormalizePath(directoryPath), out var volume))
				{
					volumeId = volume.VolumeId;
					relativePath = volume.RelativePath;
					return true;
				}

				volumeId = string.Empty;
				relativePath = string.Empty;
				return false;
			}
		}

		private sealed class TemporaryDirectory : IDisposable
		{
			public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Files.Tests.{Guid.NewGuid():N}");

			public TemporaryDirectory()
			{
				Directory.CreateDirectory(Path);
			}

			public void Dispose()
			{
				Directory.Delete(Path, true);
			}
		}
	}
}
