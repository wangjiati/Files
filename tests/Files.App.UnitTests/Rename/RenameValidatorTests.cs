// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class RenameValidatorTests
	{
		[TestMethod]
		public void Validate_IllegalCharacter_ReturnsIllegalCharacter()
		{
			Assert.AreEqual(RenameValidationError.IllegalCharacter, RenameValidator.Validate("京都_2056?.jpg"));
		}

		[TestMethod]
		public void Validate_ReservedNameWithExtension_ReturnsReservedName()
		{
			Assert.AreEqual(RenameValidationError.ReservedName, RenameValidator.Validate("CON.txt"));
		}

		[TestMethod]
		public void Validate_ReservedNameWithoutExtension_ReturnsReservedName()
		{
			Assert.AreEqual(RenameValidationError.ReservedName, RenameValidator.Validate("LPT1"));
		}

		[TestMethod]
		public void Validate_TrailingDot_ReturnsTrailingDotOrSpace()
		{
			Assert.AreEqual(RenameValidationError.TrailingDotOrSpace, RenameValidator.Validate("name."));
		}

		[TestMethod]
		public void Validate_TrailingSpace_ReturnsTrailingDotOrSpace()
		{
			Assert.AreEqual(RenameValidationError.TrailingDotOrSpace, RenameValidator.Validate("name "));
		}

		[TestMethod]
		public void Validate_NameLongerThan255_ReturnsTooLong()
		{
			Assert.AreEqual(RenameValidationError.TooLong, RenameValidator.Validate(new string('a', 256) + ".jpg"));
		}

		[TestMethod]
		public void Validate_EmptyName_ReturnsEmpty()
		{
			Assert.AreEqual(RenameValidationError.Empty, RenameValidator.Validate(string.Empty));
		}

		[TestMethod]
		public void Validate_ValidNames_ReturnsNull()
		{
			Assert.IsNull(RenameValidator.Validate("京都_2053 (2).jpg"));
			Assert.IsNull(RenameValidator.Validate("img.jpg"));
		}
	}
}
