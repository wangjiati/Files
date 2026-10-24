// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class ShellIntegrationManifestTests
	{
		private static readonly string[] ExpectedExcludedKeys =
		[
			@"HKEY_CURRENT_USER\Software\Classes\Folder\shell\open\command",
			@"HKEY_CURRENT_USER\Software\Classes\Folder\shell\explore\command",
			@"HKEY_CURRENT_USER\Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command",
			@"HKEY_CURRENT_USER\Software\Classes\CLSID\{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}",
			@"HKEY_CURRENT_USER\Software\Classes\CLSID\{C0B4E2F3-BA21-4773-8DBA-335EC946EB8B}",
		];

		[TestMethod]
		public void Manifest_ExcludesOnlyTheShellIntegrationRegistryKeys()
		{
			var manifestPath = FindRepositoryFile(Path.Combine("src", "Files.App", "Package.appxmanifest"));
			var document = XDocument.Load(manifestPath);
			var virtualizationNamespace = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/virtualization/windows10");
			var desktop6Namespace = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/desktop/windows10/6");

			Assert.IsFalse(
				document.Descendants(desktop6Namespace + "RegistryWriteVirtualization").Any(element => element.Value.Trim() == "disabled"),
				"Registry write virtualization must not be disabled for the whole package.");

			var excludedKeys = document
				.Descendants(virtualizationNamespace + "ExcludedKey")
				.Select(element => element.Value.Trim())
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var expectedKey in ExpectedExcludedKeys)
				Assert.IsTrue(excludedKeys.Contains(expectedKey), $"Missing registry virtualization exclusion: {expectedKey}");
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Files.slnx")))
				directory = directory.Parent;

			Assert.IsNotNull(directory, "Unable to locate the repository root.");
			var path = Path.Combine(directory.FullName, relativePath);
			Assert.IsTrue(File.Exists(path), $"Expected repository file not found: {path}");
			return path;
		}
	}
}
