// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Files.App.ViewModels.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.UnitTests.Exporting
{
	[TestClass]
	public sealed class ExportFileListDialogViewModelTests
	{
		private static ExportFileListDialogViewModel Create(
			bool isSearchResultsPage = false,
			bool hasActiveFilter = false,
			bool supportsRecursion = true)
			=> new(isSearchResultsPage, hasActiveFilter, supportsRecursion, localize: key => "L:" + key);

		[TestMethod]
		public void SearchPage_LocksFilterOnAndDisablesBothOptions()
		{
			var vm = Create(isSearchResultsPage: true, hasActiveFilter: true, supportsRecursion: true);

			Assert.IsFalse(vm.IsFilterOptionEnabled);
			Assert.IsTrue(vm.ApplyCurrentFilter, "filter stays applied: the whole view is the query result");
			Assert.IsFalse(vm.IsRecursionEnabled);
			Assert.IsFalse(vm.IncludeSubfolders);
		}

		[TestMethod]
		public void NoActiveFilter_DisablesFilterOption_RecursionStillAvailable()
		{
			var vm = Create(isSearchResultsPage: false, hasActiveFilter: false, supportsRecursion: true);

			Assert.IsFalse(vm.IsFilterOptionEnabled);
			Assert.IsTrue(vm.IsRecursionEnabled);
		}

		[TestMethod]
		public void ActiveFilter_EnablesFilterOption()
		{
			var vm = Create(isSearchResultsPage: false, hasActiveFilter: true, supportsRecursion: true);

			Assert.IsTrue(vm.IsFilterOptionEnabled);
			Assert.IsTrue(vm.ApplyCurrentFilter, "defaults to exporting what the view shows");
		}

		[TestMethod]
		public void UnsupportedRecursion_DisablesRecursiveOption()
		{
			var vm = Create(isSearchResultsPage: false, hasActiveFilter: false, supportsRecursion: false);

			Assert.IsFalse(vm.IsRecursionEnabled);
		}

		[TestMethod]
		public void DefaultSelection_MatchesCatalogDefaults_AndFormatIsCsv()
		{
			var vm = Create();

			CollectionAssert.AreEqual(FileListExportColumns.All.ToList(), vm.Columns.Select(c => c.Column).ToList());
			var defaults = new HashSet<FileListExportColumn>(FileListExportColumns.DefaultSelection);
			foreach (var column in vm.Columns)
				Assert.AreEqual(defaults.Contains(column.Column), column.IsChecked, $"default check state for {column.Column}");
			Assert.AreEqual(FileListExportFormat.Csv, vm.Format);
		}

		[TestMethod]
		public void UncheckingAllColumns_DisablesExport()
		{
			var vm = Create();

			foreach (var column in vm.Columns)
				column.IsChecked = false;

			Assert.IsFalse(vm.CanExport);
		}

		[TestMethod]
		public void TogglingOneColumn_ReEnablesExport()
		{
			var vm = Create();

			foreach (var column in vm.Columns)
				column.IsChecked = false;
			Assert.IsFalse(vm.CanExport);

			vm.Columns[0].IsChecked = true;

			Assert.IsTrue(vm.CanExport);
			StringAssert.StartsWith(vm.Columns[0].Label, "L:", "labels resolve through the injected localizer");
		}
	}
}
