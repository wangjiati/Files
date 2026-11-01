// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Enums;
using Files.App.Utils;
using Files.App.Utils.Rename;
using Files.App.Utils.Storage;
using Files.App.ViewModels.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class BatchRenameViewModelTests
	{
		private static readonly DateTimeOffset CreatedUtc = new(2026, 11, 1, 9, 5, 7, TimeSpan.Zero);
		private static readonly DateTimeOffset ModifiedUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

		private static RenameTarget FileTarget(string name)
		{
			return new RenameTarget($@"C:\pic\{name}", name, false, CreatedUtc, ModifiedUtc, "pic");
		}

		private static RenameTarget FolderTarget(string name)
		{
			return new RenameTarget($@"C:\pic\{name}", name, true, CreatedUtc, ModifiedUtc, "pic");
		}

		private static BatchRenameViewModel CreateVm(IRenameScopeResolver resolver, IRenameExecutor? executor = null)
		{
			var vm = new BatchRenameViewModel(resolver, executor ?? new FakeRenameExecutor());
			vm.SetSelection([], null);
			return vm;
		}

		private sealed class FakeScopeResolver : IRenameScopeResolver
		{
			public IReadOnlyList<RenameTarget> Targets { get; set; } = [];

			public Exception? Throw { get; set; }

			public int CallCount { get; private set; }

			public Task<IReadOnlyList<RenameTarget>> ResolveAsync(RenameScopeOption option, int depth, bool includeFiles, bool includeFolders, IReadOnlyList<ListedItem> selectedItems, ListedItem? currentFolderItem, CancellationToken ct)
			{
				CallCount++;
				return Throw is null ? Task.FromResult(Targets) : Task.FromException<IReadOnlyList<RenameTarget>>(Throw);
			}
		}

		private sealed class FakeRenameExecutor : IRenameExecutor
		{
			public List<IReadOnlyList<RenameExecutionItem>> Plans { get; } = [];

			public IFilesystemHelpers? ReceivedFilesystem { get; private set; }

			public Task<RenameExecutionResult> ExecuteAsync(IReadOnlyList<RenameExecutionItem> plan, IFilesystemHelpers fs, IProgress<string>? progress, CancellationToken ct)
			{
				Plans.Add(plan);
				ReceivedFilesystem = fs;
				return Task.FromResult(new RenameExecutionResult([]));
			}
		}

		private sealed class StubFilesystemHelpers : IFilesystemHelpers
		{
			public void Dispose() { }

			public Task<(ReturnResult, IStorageItem?)> CreateAsync(IStorageItemWithPath source, bool registerHistory)
				=> Task.FromResult<(ReturnResult, IStorageItem?)>((ReturnResult.Cancelled, null));

			public Task<ReturnResult> DeleteItemsAsync(IEnumerable<IStorageItem> source, DeleteConfirmationPolicies showDialog, bool permanently, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> DeleteItemAsync(IStorageItem source, DeleteConfirmationPolicies showDialog, bool permanently, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> DeleteItemsAsync(IEnumerable<IStorageItemWithPath> source, DeleteConfirmationPolicies showDialog, bool permanently, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> DeleteItemAsync(IStorageItemWithPath source, DeleteConfirmationPolicies showDialog, bool permanently, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RestoreItemFromTrashAsync(IStorageItem source, string destination, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RestoreItemsFromTrashAsync(IEnumerable<IStorageItem> source, IEnumerable<string> destination, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RestoreItemFromTrashAsync(IStorageItemWithPath source, string destination, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RestoreItemsFromTrashAsync(IEnumerable<IStorageItemWithPath> source, IEnumerable<string> destination, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> PerformOperationTypeAsync(DataPackageOperation operation, DataPackageView packageView, string destination, bool showDialog, bool registerHistory, bool isDestinationExecutable = false, bool isDestinationScript = false)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CopyItemsAsync(IEnumerable<IStorageItem> source, IEnumerable<string> destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CopyItemAsync(IStorageItem source, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CopyItemsAsync(IEnumerable<IStorageItemWithPath> source, IEnumerable<string> destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CopyItemAsync(IStorageItemWithPath source, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CopyItemsFromClipboard(DataPackageView packageView, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RecycleItemsFromClipboard(DataPackageView packageView, string destination, DeleteConfirmationPolicies showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> CreateShortcutFromClipboard(DataPackageView packageView, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> MoveItemsAsync(IEnumerable<IStorageItem> source, IEnumerable<string> destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> MoveItemAsync(IStorageItem source, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> MoveItemsAsync(IEnumerable<IStorageItemWithPath> source, IEnumerable<string> destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> MoveItemAsync(IStorageItemWithPath source, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> MoveItemsFromClipboard(DataPackageView packageView, string destination, bool showDialog, bool registerHistory)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RenameAsync(IStorageItem source, string newName, NameCollisionOption collision, bool registerHistory, bool showExtensionDialog = true)
				=> Task.FromResult(ReturnResult.Cancelled);

			public Task<ReturnResult> RenameAsync(IStorageItemWithPath source, string newName, NameCollisionOption collision, bool registerHistory, bool showExtensionDialog = true)
				=> Task.FromResult(ReturnResult.Cancelled);
		}

		[TestMethod]
		public async Task RebuildPreview_PreservesManualName_AcrossFindChangeAndTemplateSwitch()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg"), FileTarget("IMG_002.jpg")] });
			vm.Rule = vm.Rule with { Find = "IMG", ReplaceWith = "照片" };
			await vm.ResolveScopeAsync();

			vm.Rows[0].SetManual("手改_001.jpg");

			vm.Rule = vm.Rule with { Find = "00" };
			vm.RebuildPreview();

			Assert.AreEqual("手改_001.jpg", vm.Rows[0].ManualName);
			Assert.AreEqual(RenameRowStatus.Manual, vm.Rows[0].Status);
			Assert.AreEqual("IMG_照片2.jpg", vm.Rows[1].FinalName);

			vm.ApplyTemplate(RenameTemplate.Prepend);
			vm.RebuildPreview();

			Assert.AreEqual(RenameTemplate.Prepend, vm.Rule.Template);
			Assert.AreEqual("手改_001.jpg", vm.Rows[0].ManualName);
			Assert.AreEqual(RenameRowStatus.Manual, vm.Rows[0].Status);

			vm.Rows[0].ResetManual();
			vm.RebuildPreview();

			Assert.IsNull(vm.Rows[0].ManualName);
			Assert.AreEqual(RenameRowStatus.Unchanged, vm.Rows[0].Status);
			Assert.AreEqual("IMG_001.jpg", vm.Rows[0].FinalName);
		}

		[TestMethod]
		public async Task ApplyPatternPreset_ReplacesReplaceWithOnly()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg")] });
			vm.Rule = vm.Rule with { Find = "IMG", ReplaceWith = "x", CaseConversion = RenameCaseConversion.Uppercase };
			await vm.ResolveScopeAsync();
			vm.Rows[0].SetManual("m.jpg");
			vm.Scope = RenameScopeOption.CurrentFolder;

			vm.ApplyPatternPreset("照片_${padding=3;start=1}");

			Assert.AreEqual("照片_${padding=3;start=1}", vm.Rule.ReplaceWith);
			Assert.AreEqual("IMG", vm.Rule.Find);
			Assert.AreEqual(RenameCaseConversion.Uppercase, vm.Rule.CaseConversion);
			Assert.AreEqual(RenameScopeOption.CurrentFolder, vm.Scope);
			Assert.AreEqual("m.jpg", vm.Rows[0].ManualName);
		}

		[TestMethod]
		public async Task BrokenRegexFind_MarksRuleBroken_AndDisablesApply()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg")] });
			await vm.ResolveScopeAsync();
			Assert.IsFalse(vm.IsRuleBroken);

			vm.Rule = vm.Rule with { Mode = RenameMode.Regex, Find = "(" };
			vm.RebuildPreview();

			Assert.IsTrue(vm.IsRuleBroken);
			Assert.AreEqual(RenameRowStatus.Error, vm.Rows[0].Status);
			Assert.IsFalse(vm.ApplyCommand.CanExecute(null));
		}

		[TestMethod]
		public async Task InvalidNameRow_MarksRowError_ButApplyStaysUsable()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("a.txt"), FileTarget("ba.txt")] });
			// "a" → "CON" produces the reserved device name "CON.txt" (row error),
			// while "ba" → "bCON" stays valid, so one executable row remains.
			vm.Rule = vm.Rule with { Find = "a", ReplaceWith = "CON" };
			await vm.ResolveScopeAsync();

			Assert.IsFalse(vm.IsRuleBroken);
			Assert.AreEqual(RenameRowStatus.Error, vm.Rows[0].Status);
			Assert.AreEqual(RenameRowStatus.Changed, vm.Rows[1].Status);
			Assert.IsTrue(vm.ApplyCommand.CanExecute(null));
		}

		[TestMethod]
		public async Task UncheckedRow_IsExcludedFromExecutionPlan()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg"), FileTarget("IMG_002.jpg")] });
			vm.Rule = vm.Rule with { Find = "IMG", ReplaceWith = "照片" };
			await vm.ResolveScopeAsync();
			vm.Rows[0].IsIncluded = false;

			var plan = RenameExecutor.BuildExecutionPlan(vm.Rows, vm.ConflictStrategy);

			Assert.AreEqual(1, plan.Count);
			Assert.AreEqual(vm.Rows[1].Target.FullPath, plan[0].SourcePath);
		}

		[TestMethod]
		public async Task FolderRow_ReasonFollowsIncludeFolders()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg"), FolderTarget("sub")] });
			vm.Rule = vm.Rule with { Find = "s", ReplaceWith = "t" };
			await vm.ResolveScopeAsync();

			Assert.IsFalse(vm.IncludeFolders);
			RenamePreviewRow folderRow = vm.Rows.Single(r => r.Target.IsFolder);
			Assert.AreEqual(RenameRowStatus.Unchanged, folderRow.Status);
			Assert.AreEqual(UnchangedReason.ExcludedFolderType, folderRow.UnchangedReason);
			RenamePreviewRow fileRow = vm.Rows.Single(r => !r.Target.IsFolder);
			Assert.AreEqual(UnchangedReason.NotMatched, fileRow.UnchangedReason);

			vm.IncludeFolders = true;
			vm.RebuildPreview();

			folderRow = vm.Rows.Single(r => r.Target.IsFolder);
			Assert.AreEqual(RenameRowStatus.Changed, folderRow.Status);
			Assert.AreEqual("tub", folderRow.FinalName);
			Assert.IsNull(folderRow.UnchangedReason);
			fileRow = vm.Rows.Single(r => !r.Target.IsFolder);
			Assert.AreEqual(UnchangedReason.NotMatched, fileRow.UnchangedReason);
		}

		[TestMethod]
		public async Task PasteNewNames_SkipsEmptyLines_AndFlagsTruncation()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("a.jpg"), FileTarget("b.jpg"), FileTarget("c.jpg")] });
			await vm.ResolveScopeAsync();

			vm.PasteNewNames("x\r\n\r\nz");

			Assert.AreEqual("x", vm.Rows[0].ManualName);
			Assert.AreEqual(RenameRowStatus.Manual, vm.Rows[0].Status);
			Assert.IsNull(vm.Rows[1].ManualName);
			Assert.AreEqual("z", vm.Rows[2].ManualName);
			Assert.IsFalse(vm.PasteTruncated);

			vm.PasteNewNames("1\n2\n3\n4\n5");

			Assert.AreEqual("1", vm.Rows[0].ManualName);
			Assert.AreEqual("3", vm.Rows[2].ManualName);
			Assert.IsTrue(vm.PasteTruncated);
		}

		[TestMethod]
		public async Task CopyOriginalNames_JoinsNamesInRowOrder()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("a.jpg"), FileTarget("b.jpg"), FileTarget("c.jpg")] });
			await vm.ResolveScopeAsync();

			Assert.AreEqual("a.jpg\r\nb.jpg\r\nc.jpg", vm.CopyOriginalNames());
		}

		[TestMethod]
		public async Task PreviewFilter_Changed_HidesUnchangedRows()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg"), FileTarget("other.txt")] });
			vm.Rule = vm.Rule with { Find = "IMG", ReplaceWith = "照片" };
			await vm.ResolveScopeAsync();

			vm.PreviewFilter = PreviewFilter.All;
			Assert.AreEqual(2, vm.VisibleRows.Count);

			vm.PreviewFilter = PreviewFilter.Changed;
			Assert.AreEqual(1, vm.VisibleRows.Count);
			Assert.AreEqual("IMG_001.jpg", vm.VisibleRows[0].Target.Name);
		}

		[TestMethod]
		public async Task SearchText_MatchesOriginalFinalNameAndPosition()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("aa.jpg"), FileTarget("bb.jpg")] });
			vm.Rule = vm.Rule with { Find = "aa", ReplaceWith = "zz" };
			await vm.ResolveScopeAsync();
			vm.PreviewFilter = PreviewFilter.All;

			vm.SearchText = "bb";
			Assert.AreEqual(1, vm.VisibleRows.Count);
			Assert.AreEqual("bb.jpg", vm.VisibleRows[0].Target.Name);

			vm.SearchText = "ZZ";
			Assert.AreEqual(1, vm.VisibleRows.Count);
			Assert.AreEqual("aa.jpg", vm.VisibleRows[0].Target.Name);
			Assert.AreEqual("zz.jpg", vm.VisibleRows[0].FinalName);

			vm.SearchText = "2";
			Assert.AreEqual(1, vm.VisibleRows.Count);
			Assert.AreEqual("bb.jpg", vm.VisibleRows[0].Target.Name);

			vm.SearchText = "cc";
			Assert.AreEqual(0, vm.VisibleRows.Count);
		}

		[TestMethod]
		public async Task Apply_BuildsPlan_AndCallsInjectedExecutor()
		{
			var executor = new FakeRenameExecutor();
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("IMG_001.jpg"), FileTarget("IMG_002.jpg")] }, executor);
			vm.Rule = vm.Rule with { Find = "IMG", ReplaceWith = "照片" };
			await vm.ResolveScopeAsync();
			var filesystem = new StubFilesystemHelpers();
			vm.Filesystem = filesystem;

			await vm.ApplyCommand.ExecuteAsync(null);

			Assert.AreEqual(1, executor.Plans.Count);
			IReadOnlyList<RenameExecutionItem> plan = executor.Plans[0];
			Assert.AreEqual(2, plan.Count);
			Assert.AreEqual(vm.Rows[0].Target.FullPath, plan[0].SourcePath);
			Assert.IsTrue(plan[0].DestinationPath.EndsWith(@"\照片_001.jpg", StringComparison.Ordinal));
			Assert.AreEqual(vm.Rows[1].Target.FullPath, plan[1].SourcePath);
			Assert.IsTrue(plan[1].DestinationPath.EndsWith(@"\照片_002.jpg", StringComparison.Ordinal));
			Assert.AreSame(filesystem, executor.ReceivedFilesystem);
		}

		[TestMethod]
		public async Task ResolveScope_Failure_SetsErrorStateWithoutThrowing()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("a.jpg")], Throw = new IOException("boom") });

			await vm.ResolveScopeAsync();

			Assert.IsFalse(vm.IsResolving);
			Assert.IsNotNull(vm.ResolveError);
			Assert.AreEqual(0, vm.Rows.Count);
		}

		[TestMethod]
		public async Task ResolveScope_Cancellation_IsSilentlyIgnored()
		{
			var vm = CreateVm(new FakeScopeResolver { Targets = [FileTarget("a.jpg")], Throw = new OperationCanceledException() });

			await vm.ResolveScopeAsync();

			Assert.IsFalse(vm.IsResolving);
			Assert.IsNull(vm.ResolveError);
		}
	}
}
