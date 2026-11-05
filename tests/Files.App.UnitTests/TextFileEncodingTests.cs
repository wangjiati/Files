// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class TextFileEncodingTests
	{
		[TestMethod]
		public void Decode_ReadsGbkWithoutByteOrderMark()
		{
			// "你好，世界" in GBK. The bytes are not valid UTF-8, so this is the legacy path.
			byte[] bytes =
			[
				0xC4, 0xE3,
				0xBA, 0xC3,
				0xA3, 0xAC,
				0xCA, 0xC0,
				0xBD, 0xE7,
			];

			Assert.AreEqual("你好，世界", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_ReadsUtf8WithoutByteOrderMark()
		{
			var bytes = Encoding.UTF8.GetBytes("你好, world");

			Assert.AreEqual("你好, world", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_StripsUtf8ByteOrderMark()
		{
			var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("hello")).ToArray();

			Assert.AreEqual("hello", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_ReadsUtf16LittleEndianByteOrderMark()
		{
			var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("你好")).ToArray();

			Assert.AreEqual("你好", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_ReadsUtf16BigEndianByteOrderMark()
		{
			var bytes = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("你好")).ToArray();

			Assert.AreEqual("你好", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_ReadsUtf32ByteOrderMark()
		{
			var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
			var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("你好")).ToArray();

			Assert.AreEqual("你好", TextFileEncoding.Decode(bytes));
		}

		[TestMethod]
		public void Decode_ReadsPlainAscii()
		{
			var bytes = Encoding.ASCII.GetBytes("line 1\r\nline 2\r\n");

			Assert.AreEqual("line 1\r\nline 2\r\n", TextFileEncoding.Decode(bytes));
		}
	}
}
