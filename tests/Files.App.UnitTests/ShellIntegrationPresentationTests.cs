// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class ShellIntegrationPresentationTests
	{
		[TestMethod]
		public void OpenDialogSettings_AreVisibleWheneverSystemIntegrationIsSupported()
		{
			Assert.IsTrue(ShellIntegrationSettingsPresentation.ShowOpenFileDialogSettings(supportsSystemIntegration: true));
			Assert.IsFalse(ShellIntegrationSettingsPresentation.ShowOpenFileDialogSettings(supportsSystemIntegration: false));
			Assert.IsTrue(ShellIntegrationSettingsPresentation.ShowDefaultFileManagerSettings(supportsSystemIntegration: true));
		}

		[TestMethod]
		public void DefaultFileManagerStatus_MapsEveryOwner()
		{
			Assert.AreEqual(
				ShellIntegrationStatusResource.DefaultFileManagerInternal,
				ShellIntegrationSettingsPresentation.GetDefaultFileManagerStatus(ShellIntegrationOwner.Internal));
			Assert.AreEqual(
				ShellIntegrationStatusResource.DefaultFileManagerWindows,
				ShellIntegrationSettingsPresentation.GetDefaultFileManagerStatus(ShellIntegrationOwner.Windows));
			Assert.AreEqual(
				ShellIntegrationStatusResource.DefaultFileManagerOther,
				ShellIntegrationSettingsPresentation.GetDefaultFileManagerStatus(ShellIntegrationOwner.Other));
			Assert.AreEqual(
				ShellIntegrationStatusResource.DefaultFileManagerNone,
				ShellIntegrationSettingsPresentation.GetDefaultFileManagerStatus(ShellIntegrationOwner.None));
		}

		[TestMethod]
		public void OpenDialogStatus_MapsEveryOwner()
		{
			Assert.AreEqual(
				ShellIntegrationStatusResource.OpenFileDialogInternal,
				ShellIntegrationSettingsPresentation.GetOpenFileDialogStatus(ShellIntegrationOwner.Internal));
			Assert.AreEqual(
				ShellIntegrationStatusResource.OpenFileDialogOther,
				ShellIntegrationSettingsPresentation.GetOpenFileDialogStatus(ShellIntegrationOwner.Other));
			Assert.AreEqual(
				ShellIntegrationStatusResource.OpenFileDialogNone,
				ShellIntegrationSettingsPresentation.GetOpenFileDialogStatus(ShellIntegrationOwner.None));
		}
	}
}
