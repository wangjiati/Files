// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class GeneralViewModelRestartTests
	{
		[TestMethod]
		public async Task RestartFailure_DoesNotExitAndReportsError()
		{
			var exitCalls = 0;
			var errorReports = 0;

			await GeneralViewModel.CompleteRestartAsync(
				false,
				_ => exitCalls++,
				() => { errorReports++; return Task.CompletedTask; });

			Assert.AreEqual(0, exitCalls);
			Assert.AreEqual(1, errorReports);
		}

		[TestMethod]
		public async Task RestartSuccess_ExitsWithoutReportingError()
		{
			var exitCode = -1;
			var errorReports = 0;

			await GeneralViewModel.CompleteRestartAsync(
				true,
				code => exitCode = code,
				() => { errorReports++; return Task.CompletedTask; });

			Assert.AreEqual(0, exitCode);
			Assert.AreEqual(0, errorReports);
		}
	}
}
