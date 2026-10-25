// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Helpers;
using Files.App.Services.DateTimeFormatter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DateTimeFormatterTests
	{
		private readonly DateTimeFormatterFactory _factory = new();

		[TestInitialize]
		public void Initialize()
		{
			AppRuntime.Initialize(DistributionKind.Portable, Environment.CurrentDirectory);
		}

		[TestCleanup]
		public void Cleanup()
		{
			AppRuntime.Reset();
		}

		[TestMethod]
		public void CustomFormatter_FormatsMilliseconds()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff");

			Assert.AreEqual("2024-03-05 14:07:09.123", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		[TestMethod]
		public void CustomFormatter_UsesTwentyFourHourClock()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss");

			Assert.AreEqual("2024-03-05 14:07:09", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		[TestMethod]
		public void CustomFormatter_UsesTwelveHourClock()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd hh:mm:ss");

			Assert.AreEqual("2024-03-05 02:07:09", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		[TestMethod]
		public void CustomFormatter_ConvertsToLocalTime()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff");
			var utc = new DateTimeOffset(2024, 3, 5, 14, 7, 9, 123, TimeSpan.Zero);

			var culture = new CultureInfo(AppLanguageHelper.PreferredLanguage.Code);

			Assert.AreEqual(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", culture), formatter.ToShortLabel(utc));
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankForInvalidYears()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff");

			Assert.AreEqual(" ", formatter.ToShortLabel(LocalTime(1601, 1, 1, 0, 0, 0, 0)));
			Assert.AreEqual(" ", formatter.ToShortLabel(LocalTime(9999, 12, 31, 23, 59, 59, 999)));
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankInsteadOfThrowingForInvalidPattern()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd 'HH");

			Assert.AreEqual(" ", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		[TestMethod]
		public void RuleValidate_ReturnsErrorForInvalidCustomPattern()
		{
			var rule = new DateTimeFormatRule(
				DateTimeColumnKind.Modified,
				@"C:\Users\Test",
				false,
				DateTimeFormats.Custom,
				"yyyy-MM-dd 'HH");

			Assert.IsFalse(string.IsNullOrWhiteSpace(rule.Validate()));
		}

		[TestMethod]
		public void RuleValidate_ReturnsErrorForMissingCustomPattern()
		{
			var rule = new DateTimeFormatRule(null, null, false, DateTimeFormats.Custom, " ");

			Assert.IsFalse(string.IsNullOrWhiteSpace(rule.Validate()));
		}

		[TestMethod]
		public void RuleValidate_ReturnsNullForValidRules()
		{
			var customRule = new DateTimeFormatRule(
				DateTimeColumnKind.Created,
				@"C:\Users\Test",
				true,
				DateTimeFormats.Custom,
				"yyyy-MM-dd HH:mm:ss.fff");
			var presetRule = new DateTimeFormatRule(
				DateTimeColumnKind.Deleted,
				null,
				false,
				DateTimeFormats.Universal,
				null);

			Assert.IsNull(customRule.Validate());
			Assert.IsNull(presetRule.Validate());
		}

		[TestMethod]
		public void Factory_PreservesExistingPresetBehavior()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Universal);

			Assert.AreEqual("2024-03-05 14:07:09", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
		{
			var value = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
			return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
		}
	}
}
