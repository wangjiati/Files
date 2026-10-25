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
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);
			var localTime = local.ToLocalTime();
			var culture = AppCulture();
			var result = formatter.ToShortLabel(local);

			Assert.AreEqual(localTime.ToString("yyyy-MM-dd HH:mm:ss.fff", culture), result);
			Assert.IsTrue(result.EndsWith($".{localTime.ToString("fff", culture)}", StringComparison.Ordinal));
		}

		[TestMethod]
		public void CustomFormatter_UsesTwentyFourHourClock()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss");
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);
			var localTime = local.ToLocalTime();
			var culture = AppCulture();

			Assert.AreEqual(localTime.ToString("yyyy-MM-dd HH:mm:ss", culture), formatter.ToShortLabel(local));
		}

		[TestMethod]
		public void CustomFormatter_UsesTwelveHourClock()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd hh:mm:ss");
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);
			var localTime = local.ToLocalTime();
			var culture = AppCulture();
			var expectedTwelveHour = localTime.ToString("yyyy-MM-dd hh:mm:ss", culture);

			Assert.AreEqual(expectedTwelveHour, formatter.ToShortLabel(local));
			Assert.AreNotEqual(localTime.ToString("yyyy-MM-dd HH:mm:ss", culture), expectedTwelveHour);
		}

		[TestMethod]
		public void CustomFormatter_ConvertsToLocalTime()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff");
			var utc = new DateTimeOffset(2024, 3, 5, 14, 7, 9, 123, TimeSpan.Zero);

			Assert.AreEqual(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", AppCulture()), formatter.ToShortLabel(utc));
		}

		[TestMethod]
		public void CustomFormatter_PreservesLiteralText()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd 'at' HH:mm:ss.fff");
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);
			var result = formatter.ToShortLabel(local);

			Assert.AreEqual(local.ToLocalTime().ToString("yyyy-MM-dd 'at' HH:mm:ss.fff", AppCulture()), result);
			Assert.IsTrue(result.Contains("at", StringComparison.Ordinal));
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankForInvalidYears()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd HH:mm:ss.fff");

			Assert.AreEqual(" ", formatter.ToShortLabel(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)));
			Assert.AreEqual(" ", formatter.ToShortLabel(new DateTimeOffset(9999, 12, 31, 23, 59, 59, 999, TimeSpan.Zero)));
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankForEmptyPattern()
		{
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);

			foreach (var pattern in new string?[] { null, string.Empty, " " })
			{
				var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, pattern);
				Assert.AreEqual(" ", formatter.ToShortLabel(local), $"Pattern: {pattern ?? "<null>"}");
			}
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankInsteadOfThrowingForInvalidPattern()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Custom, "yyyy-MM-dd 'HH");

			Assert.AreEqual(" ", formatter.ToShortLabel(LocalTime(2024, 3, 5, 14, 7, 9, 123)));
		}

		[TestMethod]
		public void CustomFormatter_ReturnsBlankWhenCalendarCannotRepresentDate()
		{
			var culture = new CultureInfo("ar-SA");
			culture.DateTimeFormat.Calendar = new UmAlQuraCalendar();
			var formatter = new CustomDateTimeFormatter("yyyy-MM-dd HH:mm:ss.fff", culture);
			var outOfRange = new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

			Assert.AreEqual(" ", formatter.ToShortLabel(outOfRange));
		}

		[TestMethod]
		public void RuleValidate_ReturnsInvalidPatternForInvalidCustomPattern()
		{
			var rule = new DateTimeFormatRule(
				DateTimeColumnKind.Modified,
				@"C:\Users\Test",
				false,
				DateTimeFormats.Custom,
				"yyyy-MM-dd 'HH");

			Assert.AreEqual(DateTimeFormatRuleValidationError.InvalidPattern, rule.Validate());
		}

		[TestMethod]
		public void RuleValidate_ReturnsMissingPatternForEmptyCustomPattern()
		{
			foreach (var pattern in new string?[] { null, string.Empty, " " })
			{
				var rule = new DateTimeFormatRule(null, null, false, DateTimeFormats.Custom, pattern);
				Assert.AreEqual(DateTimeFormatRuleValidationError.MissingPattern, rule.Validate(), $"Pattern: {pattern ?? "<null>"}");
			}
		}

		[TestMethod]
		public void RuleValidate_ReturnsNoneForValidRules()
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

			Assert.AreEqual(DateTimeFormatRuleValidationError.None, customRule.Validate());
			Assert.AreEqual(DateTimeFormatRuleValidationError.None, presetRule.Validate());
		}

		[TestMethod]
		public void RuleValidate_IgnoresCustomPatternForPresetFormat()
		{
			var rule = new DateTimeFormatRule(
				DateTimeColumnKind.Modified,
				null,
				false,
				DateTimeFormats.Universal,
				"yyyy-MM-dd 'HH");

			Assert.AreEqual(DateTimeFormatRuleValidationError.None, rule.Validate());
		}

		[TestMethod]
		public void RuleValidate_ReturnsInvalidFormatForUndefinedFormat()
		{
			var rule = new DateTimeFormatRule(
				DateTimeColumnKind.Modified,
				null,
				false,
				(DateTimeFormats)99,
				null);

			Assert.AreEqual(DateTimeFormatRuleValidationError.InvalidFormat, rule.Validate());
		}

		[TestMethod]
		public void RuleValidate_ReturnsInvalidColumnForUndefinedColumn()
		{
			var rule = new DateTimeFormatRule(
				(DateTimeColumnKind)99,
				null,
				false,
				DateTimeFormats.Universal,
				null);

			Assert.AreEqual(DateTimeFormatRuleValidationError.InvalidColumn, rule.Validate());
		}

		[TestMethod]
		public void Factory_PreservesExistingPresetBehavior()
		{
			var formatter = _factory.GetDateTimeFormatter(DateTimeFormats.Universal, "yyyy-MM-dd 'HH");
			var local = LocalTime(2024, 3, 5, 14, 7, 9, 123);

			Assert.AreEqual(local.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", AppCulture()), formatter.ToShortLabel(local));
		}

		private static CultureInfo AppCulture()
			=> new(AppLanguageHelper.PreferredLanguage.Code);

		private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
		{
			var value = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
			return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
		}
	}
}
