// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class PortableLanguageTests
	{
		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
		}

		[TestMethod]
		public void Portable_UsesCultureWithoutPackageLanguageApis()
		{
			AppRuntime.Initialize(DistributionKind.Portable, Environment.CurrentDirectory);

			Assert.IsFalse(string.IsNullOrWhiteSpace(AppLanguageHelper.PreferredLanguage.Code));
			Assert.IsFalse(AppLanguageHelper.TryChange(0));
		}
	}
}
