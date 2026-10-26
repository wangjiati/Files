// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Filtering
{
	[TestClass]
	public sealed class DirectoryFilterExecutionContextTests
	{
		private static readonly DirectoryFilterSpec Spec = new() { NameContains = "report" };

		[TestMethod]
		public void CanCommit_AcceptsMatchingContext()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);

			Assert.IsTrue(context.CanCommit(@"c:\work", Spec, 4, 7));
		}

		[TestMethod]
		public void CanCommit_RejectsStaleGeneration()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);

			Assert.IsFalse(context.CanCommit(@"C:\Work", Spec, 4, 8));
		}

		[TestMethod]
		public void CanCommit_RejectsDifferentDirectory()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);

			Assert.IsFalse(context.CanCommit(@"C:\Other", Spec, 4, 7));
		}

		[TestMethod]
		public void CanCommit_RejectsStaleSourceRevision()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);

			Assert.IsFalse(context.CanCommit(@"C:\Work", Spec, 5, 7));
		}

		[TestMethod]
		public void CanCommit_RejectsDifferentSpecInstance()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);

			Assert.IsFalse(context.CanCommit(@"C:\Work", Spec with { NameContains = "notes" }, 4, 7));
		}

		[TestMethod]
		public void Cancel_PreventsCommitAndCancelsLinkedToken()
		{
			using var context = new DirectoryFilterExecutionContext(@"C:\Work", Spec, 4, 7);
			using var linked = context.CreateLinkedTokenSource(default);

			context.Cancel();

			Assert.IsTrue(context.IsCancellationRequested);
			Assert.IsTrue(linked.Token.IsCancellationRequested);
			Assert.IsFalse(context.CanCommit(@"C:\Work", Spec, 4, 7));
		}
	}
}
