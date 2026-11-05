// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Decodes text files the way Windows does: a byte order mark wins, then strict UTF-8, then the
	/// system ANSI code page (GBK on Chinese systems) and finally GB18030. This keeps legacy files
	/// from rendering as replacement characters in the preview pane.
	/// </summary>
	internal static class TextFileEncoding
	{
		private const int Gb18030CodePage = 54936;

		private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
		private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

		static TextFileEncoding()
		{
			// Legacy code pages (GBK, Shift-JIS, ...) are not available without the provider.
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		public static string Decode(byte[] bytes)
		{
			if (TryGetPreamble(bytes, out var encoding, out var preambleLength))
				return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

			try
			{
				return StrictUtf8.GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
			}

			return DecodeLegacy(bytes);
		}

		private static string DecodeLegacy(byte[] bytes)
		{
			int ansiCodePage = CultureInfo.InstalledUICulture.TextInfo.ANSICodePage;

			if (TryDecodeStrict(ansiCodePage, bytes, out var ansiText))
				return ansiText;

			// The file is not valid in the system code page; GB18030 covers GBK text on systems
			// whose ANSI code page is not a Chinese one.
			if (TryDecodeStrict(Gb18030CodePage, bytes, out var gbText))
				return gbText;

			try
			{
				return Encoding.GetEncoding(ansiCodePage).GetString(bytes);
			}
			catch (ArgumentException)
			{
				return Utf8.GetString(bytes);
			}
		}

		private static bool TryDecodeStrict(int codePage, byte[] bytes, out string text)
		{
			text = string.Empty;

			try
			{
				var strictEncoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

				text = strictEncoding.GetString(bytes);
				return true;
			}
			catch (DecoderFallbackException)
			{
				return false;
			}
			catch (ArgumentException)
			{
				// The code page is not installed on this system.
				return false;
			}
		}

		private static bool TryGetPreamble(byte[] bytes, out Encoding encoding, out int length)
		{
			encoding = Utf8;
			length = 0;

			if (bytes.Length >= 4)
			{
				if (bytes[0] is 0x00 && bytes[1] is 0x00 && bytes[2] is 0xFE && bytes[3] is 0xFF)
				{
					encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
					length = 4;
					return true;
				}

				if (bytes[0] is 0xFF && bytes[1] is 0xFE && bytes[2] is 0x00 && bytes[3] is 0x00)
				{
					encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
					length = 4;
					return true;
				}
			}

			if (bytes.Length >= 3 && bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF)
			{
				length = 3;
				return true;
			}

			if (bytes.Length >= 2)
			{
				if (bytes[0] is 0xFF && bytes[1] is 0xFE)
				{
					encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
					length = 2;
					return true;
				}

				if (bytes[0] is 0xFE && bytes[1] is 0xFF)
				{
					encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
					length = 2;
					return true;
				}
			}

			return false;
		}
	}
}
