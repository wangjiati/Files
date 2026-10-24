// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class AppRuntimeEnvironmentTests
	{
		[TestMethod]
		public void Portable_KeepsApplicationDirectoriesBelowTheProvidedRoot()
		{
			var root = Path.Combine(Path.GetTempPath(), "Files Portable 中文");
			var normalizedRoot = Path.GetFullPath(root);

			AppRuntime.Initialize(DistributionKind.Portable, root);
			var environment = AppRuntime.Current;

			Assert.AreEqual(normalizedRoot, environment.InstallDirectory);
			Assert.AreEqual(Path.Combine(normalizedRoot, "Data"), environment.DataDirectory);
			AssertDirectoryBelow(environment.DataDirectory, environment.SettingsDirectory);
			AssertDirectoryBelow(environment.DataDirectory, environment.LogDirectory);
			AssertDirectoryBelow(environment.DataDirectory, environment.CacheDirectory);
			Assert.IsNull(environment.PackageFamilyName);
			Assert.AreEqual(DistributionKind.Portable, environment.Distribution);
			Assert.IsFalse(environment.SupportsSystemIntegration);
		}

		[TestMethod]
		public void Portable_UsesBaseDirectoryAndAssemblyVersionByDefault()
		{
			var environment = new PortableAppRuntimeEnvironment();

			Assert.AreEqual(Path.GetFullPath(AppContext.BaseDirectory), environment.InstallDirectory);
			Assert.AreEqual(typeof(AppRuntime).Assembly.GetName().Version, environment.Version);
		}

		[TestMethod]
		public void Portable_UsesDifferentInstanceScopesForDifferentRoots()
		{
			var first = new PortableAppRuntimeEnvironment(Path.Combine(Path.GetTempPath(), "Files-first"));
			var second = new PortableAppRuntimeEnvironment(Path.Combine(Path.GetTempPath(), "Files-second"));

			Assert.AreNotEqual(first.InstanceScope, second.InstanceScope);
		}

		[TestMethod]
		public void Msix_ReadsPackageMetadataOnlyDuringConstruction()
		{
			var reads = 0;
			var snapshot = new MsixAppRuntimeSnapshot(
				"Files.Internal",
				"Files - Internal",
				new Version(4, 3, 1, 0),
				@"C:\Apps\Files.Internal",
				@"C:\Users\Test\AppData\Local\Packages\Files.Internal\LocalState",
				@"C:\Users\Test\AppData\Local\Packages\Files.Internal\LocalCache",
				"Files.Internal_abc123");

			var environment = new MsixAppRuntimeEnvironment(() =>
			{
				reads++;
				return snapshot;
			});

			Assert.AreEqual(1, reads);
			Assert.AreEqual(snapshot.ProductId, environment.ProductId);
			Assert.AreEqual(snapshot.DisplayName, environment.DisplayName);
			Assert.AreEqual(snapshot.Version, environment.Version);
			Assert.AreEqual(snapshot.InstallDirectory, environment.InstallDirectory);
			Assert.AreEqual(snapshot.DataDirectory, environment.DataDirectory);
			Assert.AreEqual(Path.Combine(snapshot.DataDirectory, "settings"), environment.SettingsDirectory);
			Assert.AreEqual(snapshot.DataDirectory, environment.LogDirectory);
			Assert.AreEqual(snapshot.CacheDirectory, environment.CacheDirectory);
			Assert.AreEqual(snapshot.PackageFamilyName, environment.PackageFamilyName);
			Assert.AreEqual(snapshot.PackageFamilyName, environment.InstanceScope);
			Assert.AreEqual(DistributionKind.Msix, environment.Distribution);
			Assert.IsTrue(environment.SupportsSystemIntegration);
			Assert.AreEqual(snapshot.DisplayName, environment.DisplayName);
			Assert.AreEqual(1, reads);
		}

		private static void AssertDirectoryBelow(string root, string path)
		{
			var relative = Path.GetRelativePath(root, path);
			Assert.IsFalse(Path.IsPathRooted(relative), $"'{path}' is not below '{root}'.");
			Assert.IsFalse(relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal), $"'{path}' is not below '{root}'.");
		}
	}
}
