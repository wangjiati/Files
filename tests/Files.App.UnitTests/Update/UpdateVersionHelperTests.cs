// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.Update;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Update
{
	[TestClass]
	public sealed class UpdateVersionHelperTests
	{
		[TestMethod]
		public void TryParseVersion_AcceptsFourPartTag()
		{
			Assert.IsTrue(UpdateVersionHelper.TryParseVersion("v4.3.1.7", out var version));
			Assert.AreEqual(new Version(4, 3, 1, 7), version);
		}

		[TestMethod]
		public void TryParseVersion_PadsThreePartTag()
		{
			Assert.IsTrue(UpdateVersionHelper.TryParseVersion("4.3.1", out var version));
			Assert.AreEqual(new Version(4, 3, 1, 0), version);
		}

		[TestMethod]
		public void TryParseVersion_AcceptsHistoricalSuffix()
		{
			Assert.IsTrue(UpdateVersionHelper.TryParseVersion("v4.3.1.0-portable", out var version));
			Assert.AreEqual(new Version(4, 3, 1, 0), version);
		}

		[TestMethod]
		public void TryParseVersion_RejectsInvalidText()
		{
			Assert.IsFalse(UpdateVersionHelper.TryParseVersion(null, out _));
			Assert.IsFalse(UpdateVersionHelper.TryParseVersion("v4", out _));
			Assert.IsFalse(UpdateVersionHelper.TryParseVersion("not-a-version", out _));
		}

		[TestMethod]
		public void IsNewer_ComparesNumerically()
		{
			Assert.IsTrue(UpdateVersionHelper.IsNewer(new Version(4, 3, 1, 7), new Version(4, 3, 1, 0)));
			Assert.IsTrue(UpdateVersionHelper.IsNewer(new Version(4, 4, 0, 1), new Version(4, 3, 1, 99)));
			Assert.IsFalse(UpdateVersionHelper.IsNewer(new Version(4, 3, 1, 0), new Version(4, 3, 1, 0)));
		}
	}
}
