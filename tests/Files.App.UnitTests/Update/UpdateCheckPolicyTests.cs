// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.Update;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Update
{
	[TestClass]
	public sealed class UpdateCheckPolicyTests
	{
		[TestMethod]
		public void ShouldCheckToday_OnlyChecksWhenDateDiffers()
		{
			var today = new DateOnly(2026, 11, 7);
			Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday(null, today));
			Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday("2026-11-06", today));
			Assert.IsFalse(UpdateCheckPolicy.ShouldCheckToday("2026-11-07", today));
		}

		[TestMethod]
		public void ShouldCheckToday_IgnoresInvalidStoredValue()
		{
			Assert.IsTrue(UpdateCheckPolicy.ShouldCheckToday("not-a-date", new DateOnly(2026, 11, 7)));
		}

		[TestMethod]
		public void IsSkipped_MatchesParsedVersion()
		{
			Assert.IsTrue(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), "4.3.1.7"));
			Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 8), "4.3.1.7"));
			Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), null));
			Assert.IsFalse(UpdateCheckPolicy.IsSkipped(new Version(4, 3, 1, 7), "garbage"));
		}
	}
}
