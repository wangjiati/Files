// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Rename;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests.Rename
{
	[TestClass]
	public sealed class SubfolderDepthTests
	{
		[TestMethod]
		public void IsWithinDepth_SingleSegment_AtDepthOne_ReturnsTrue()
		{
			Assert.IsTrue(SubfolderDepth.IsWithinDepth("a", 1));
		}

		[TestMethod]
		public void IsWithinDepth_SingleSegment_AtDepthZero_ReturnsFalse()
		{
			Assert.IsFalse(SubfolderDepth.IsWithinDepth("a", 0));
		}

		[TestMethod]
		public void IsWithinDepth_ThreeSegments_AtDepthTwo_ReturnsFalse()
		{
			Assert.IsFalse(SubfolderDepth.IsWithinDepth("a\\b\\c", 2));
		}

		[TestMethod]
		public void IsWithinDepth_ThreeSegments_AtDepthThree_ReturnsTrue()
		{
			Assert.IsTrue(SubfolderDepth.IsWithinDepth("a\\b\\c", 3));
		}
	}
}
