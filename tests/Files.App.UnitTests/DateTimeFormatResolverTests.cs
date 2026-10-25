// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App;
using Files.App.Data.Enums;
using Files.App.Data.Items;
using Files.App.Services.DateTimeFormatter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Files.App.UnitTests
{
	[TestClass]
	public sealed class DateTimeFormatResolverTests
	{
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
		public void Resolve_PrefersDirectoryColumnRule()
		{
			var source = CreateSource();
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.System));
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", false, DateTimeFormats.Universal));
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", false, DateTimeFormats.Custom, "ss"));

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Custom, resolved.Format);
			Assert.AreEqual("ss", resolved.CustomPattern);
		}

		[TestMethod]
		public void Resolve_PrefersDirectoryDefaultRuleOverGlobalColumnRule()
		{
			var source = CreateSource();
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.System));
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", false, DateTimeFormats.Universal));

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
		}

		[TestMethod]
		public void Resolve_PrefersGlobalColumnRuleOverGlobalDefault()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.System));

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.System, resolved.Format);
		}

		[TestMethod]
		public void Resolve_UsesGlobalDefaultWhenNoRuleMatches()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Universal;

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Created);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
			Assert.IsNull(resolved.DirectoryPath);
			Assert.AreEqual(DateTimeColumnKind.Created, resolved.Column);
		}

		[TestMethod]
		public void Resolve_PreservesCustomPatternOfGlobalDefault()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Custom;
			source.DefaultDateTimeFormatPattern = "HH:mm:ss.fff";

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Custom, resolved.Format);
			Assert.AreEqual("HH:mm:ss.fff", resolved.CustomPattern);
		}

		[TestMethod]
		public void Resolve_DirectoryRuleWithoutRecursionRequiresExactDirectory()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", false, DateTimeFormats.Universal));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"C:\Work\Docs", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_RecursiveRuleAppliesToDescendants()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", true, DateTimeFormats.Universal));

			var resolved = CreateResolver(source).Resolve(@"C:\Work\A\B", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
		}

		[TestMethod]
		public void Resolve_ParentRecursiveColumnRuleBeatsChildDefaultRule()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", true, DateTimeFormats.System));
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work\Sub", true, DateTimeFormats.Custom, "HH:mm"));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.System, resolver.Resolve(@"C:\Work\Sub", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work\Sub", DateTimeColumnKind.Created).Format);
		}

		[TestMethod]
		public void Resolve_SimilarPathPrefixIsNotTreatedAsDescendant()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\foo", true, DateTimeFormats.Universal));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\foo\child", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"C:\foobar", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_NearestRecursiveDefaultRuleWins()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", true, DateTimeFormats.Universal));
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work\Projects", true, DateTimeFormats.Custom, "HH:mm:ss"));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work\Projects\App", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\Work\Docs", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_NearestRecursiveColumnRuleWins()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", true, DateTimeFormats.System));
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work\Projects", true, DateTimeFormats.Custom, "HH:mm"));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work\Projects\App", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.System, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Created).Format);
		}

		[TestMethod]
		public void Resolve_NormalizesCaseAndTrailingSeparators()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work\", false, DateTimeFormats.Universal));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"c:\work", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"C:\Work\Docs", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_NormalizesForwardAndDuplicateSeparators()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\\Work\\Team", false, DateTimeFormats.Universal));

			var resolved = CreateResolver(source).Resolve("c:/work/team", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
		}

		[TestMethod]
		public void Resolve_MovableVolumeRuleMatchesAfterDriveLetterChange()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(
				DateTimeColumnKind.Modified,
				@"E:\Projects",
				false,
				DateTimeFormats.Custom,
				"fff",
				volumeId: "AB12CD34",
				relativePath: @"\Projects"));

			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"F:\Projects", "ab12cd34", @"\Projects");
			volumes.AddVolume(@"F:\Projects\Sub", "ab12cd34", @"\Projects\Sub");

			var resolver = CreateResolver(source, volumes);

			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"F:\Projects", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"F:\Projects\Sub", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_MovableVolumeRecursiveRuleMatchesDescendants()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(
				null,
				@"E:\Projects",
				true,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: @"\Projects"));

			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"F:\Projects\Sub", "AB12CD34", @"\Projects\Sub");

			var resolved = CreateResolver(source, volumes).Resolve(@"F:\Projects\Sub", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
		}

		[TestMethod]
		public void Resolve_MovableVolumeRuleDoesNotBindWhenVolumeIsUnavailable()
		{
			var source = CreateSource();
			var rule = Rule(
				null,
				@"E:\Projects",
				false,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: @"\Projects");
			source.DirectoryFormatRuleList.Add(rule);
			source.DefaultDateTimeFormat = DateTimeFormats.System;

			var otherVolume = new FakeVolumePathProvider();
			otherVolume.AddVolume(@"F:\Projects", "FFFF0000", @"\Projects");

			Assert.AreEqual(DateTimeFormats.System, CreateResolver(source, otherVolume).Resolve(@"F:\Projects", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.System, CreateResolver(source, new FakeVolumePathProvider()).Resolve(@"G:\Projects", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(@"E:\Projects", rule.DirectoryPath);
			Assert.AreEqual("AB12CD34", rule.VolumeId);
		}

		[TestMethod]
		public void Resolve_SimilarVolumeRelativePathPrefixIsNotTreatedAsDescendant()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(
				null,
				@"E:\Projects",
				true,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: @"\Projects"));

			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"F:\Projects\Sub", "AB12CD34", @"\Projects\Sub");
			volumes.AddVolume(@"F:\Projects2", "AB12CD34", @"\Projects2");

			var resolver = CreateResolver(source, volumes);

			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"F:\Projects\Sub", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"F:\Projects2", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_VolumeRuleDoesNotRebindWhenDriveLetterIsReused()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.System;
			source.DirectoryFormatRuleList.Add(Rule(
				null,
				@"E:\Projects",
				true,
				DateTimeFormats.Universal,
				volumeId: "OLDVOLUME",
				relativePath: @"\Projects"));

			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "NEWVOLUME", @"\Projects");

			Assert.AreEqual(DateTimeFormats.System, CreateResolver(source, volumes).Resolve(@"E:\Projects", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Validate_RejectsVolumeRuleWithoutRelativePath()
		{
			var rule = Rule(
				null,
				@"E:\Projects",
				false,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: null);

			Assert.AreEqual(DateTimeFormatRuleValidationError.MissingRelativePath, rule.Validate());
		}

		[TestMethod]
		public void Resolve_VolumeRuleWithoutRelativePathDoesNotMatchVolumeRoot()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(
				null,
				@"E:\Projects",
				true,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: null));

			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"F:\", "AB12CD34", @"\");

			Assert.AreEqual(DateTimeFormats.Application, CreateResolver(source, volumes).Resolve(@"F:\", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_IgnoresInvalidRules()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.Custom));
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", false, DateTimeFormats.Custom));
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", false, DateTimeFormats.Universal));

			var resolved = CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified);

			Assert.AreEqual(DateTimeFormats.Universal, resolved.Format);
		}

		[TestMethod]
		public void Resolve_DuplicateRulesForSameScopeUseLastOne()
		{
			var source = CreateSource();
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.System));
			source.ColumnFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, null, false, DateTimeFormats.Universal));
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", false, DateTimeFormats.Universal));
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Modified, @"C:\Work", false, DateTimeFormats.Custom, "yyyy"));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Universal, resolver.Resolve(@"C:\Other", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_DirectoryColumnRuleForAnotherColumnDoesNotApply()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.Application;
			source.DirectoryFormatRuleList.Add(Rule(DateTimeColumnKind.Created, @"C:\Work", false, DateTimeFormats.Custom, "yyyy"));

			var resolver = CreateResolver(source);

			Assert.AreEqual(DateTimeFormats.Application, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
			Assert.AreEqual(DateTimeFormats.Custom, resolver.Resolve(@"C:\Work", DateTimeColumnKind.Created).Format);
		}

		[TestMethod]
		public void Resolve_RecursiveRuleAtDriveRootAppliesToDescendants()
		{
			var source = CreateSource();
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\", true, DateTimeFormats.Universal));

			Assert.AreEqual(DateTimeFormats.Universal, CreateResolver(source).Resolve(@"C:\Work", DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void Resolve_EmptyPathUsesGlobalDefault()
		{
			var source = CreateSource();
			source.DefaultDateTimeFormat = DateTimeFormats.System;
			source.DirectoryFormatRuleList.Add(Rule(null, @"C:\Work", true, DateTimeFormats.Universal));

			Assert.AreEqual(DateTimeFormats.System, CreateResolver(source).Resolve(string.Empty, DateTimeColumnKind.Modified).Format);
		}

		[TestMethod]
		public void NormalizeRule_StoresVolumeIdentityAndRelativePath()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "AB12CD34", @"\Projects");
			var rule = Rule(null, @"E:/Projects/", false, DateTimeFormats.Universal);

			var normalized = PortableDirectoryKey.NormalizeRule(rule, volumes);

			Assert.AreEqual(@"E:\Projects", normalized.DirectoryPath);
			Assert.AreEqual("AB12CD34", normalized.VolumeId);
			Assert.AreEqual(@"\Projects", normalized.RelativePath);
		}

		[TestMethod]
		public void NormalizeRule_KeepsVolumeIdentityWhenVolumeIsUnavailable()
		{
			var rule = Rule(
				null,
				@"E:/Projects/",
				false,
				DateTimeFormats.Universal,
				volumeId: "AB12CD34",
				relativePath: @"\Projects");

			var normalized = PortableDirectoryKey.NormalizeRule(rule, new FakeVolumePathProvider());

			Assert.AreEqual(@"E:\Projects", normalized.DirectoryPath);
			Assert.AreEqual("AB12CD34", normalized.VolumeId);
			Assert.AreEqual(@"\Projects", normalized.RelativePath);
		}

		[TestMethod]
		public void NormalizeRule_DoesNotRecomputeExistingVolumeIdentity()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Projects", "NEWVOLUME", @"\Projects");
			var rule = Rule(
				null,
				@"E:\Projects",
				false,
				DateTimeFormats.Universal,
				volumeId: "OLDVOLUME",
				relativePath: @"\Projects");

			var normalized = PortableDirectoryKey.NormalizeRule(rule, volumes);

			Assert.AreEqual("OLDVOLUME", normalized.VolumeId);
			Assert.AreEqual(@"\Projects", normalized.RelativePath);
			Assert.AreEqual(@"E:\Projects", normalized.DirectoryPath);
		}

		[TestMethod]
		public void NormalizeRule_ResetVolumeIdentityClearsIdentityForFixedPath()
		{
			var rule = Rule(
				null,
				@"E:/Projects/",
				false,
				DateTimeFormats.Universal,
				volumeId: "OLDVOLUME",
				relativePath: @"\Projects");

			var normalized = PortableDirectoryKey.NormalizeRule(rule, new FakeVolumePathProvider(), resetVolumeIdentity: true);

			Assert.AreEqual(@"E:\Projects", normalized.DirectoryPath);
			Assert.IsNull(normalized.VolumeId);
			Assert.IsNull(normalized.RelativePath);
		}

		[TestMethod]
		public void NormalizeRule_ResetVolumeIdentityMigratesToNewVolume()
		{
			var volumes = new FakeVolumePathProvider();
			volumes.AddVolume(@"E:\Other", "NEWVOLUME", @"\Other");
			var rule = Rule(
				null,
				@"E:\Other",
				false,
				DateTimeFormats.Universal,
				volumeId: "OLDVOLUME",
				relativePath: @"\Projects");

			var normalized = PortableDirectoryKey.NormalizeRule(rule, volumes, resetVolumeIdentity: true);

			Assert.AreEqual("NEWVOLUME", normalized.VolumeId);
			Assert.AreEqual(@"\Other", normalized.RelativePath);
			Assert.AreEqual(@"E:\Other", normalized.DirectoryPath);
		}

		private static FakeRuleSource CreateSource()
			=> new() { DefaultDateTimeFormat = DateTimeFormats.Application };

		private static DateTimeFormatResolver CreateResolver(FakeRuleSource source, IVolumePathProvider? volumePathProvider = null)
			=> new(source, volumePathProvider ?? new FakeVolumePathProvider());

		private static DateTimeFormatRule Rule(
			DateTimeColumnKind? column,
			string? directoryPath,
			bool includeSubdirectories,
			DateTimeFormats format,
			string? customPattern = null,
			string? volumeId = null,
			string? relativePath = null)
			=> new(column, directoryPath, includeSubdirectories, format, customPattern)
			{
				VolumeId = volumeId,
				RelativePath = relativePath,
			};

		private sealed class FakeRuleSource : IDateTimeFormatRuleSource
		{
			public List<DateTimeFormatRule> ColumnFormatRuleList { get; } = [];

			public List<DateTimeFormatRule> DirectoryFormatRuleList { get; } = [];

			public DateTimeFormats DefaultDateTimeFormat { get; set; }

			public string? DefaultDateTimeFormatPattern { get; set; }

			public IReadOnlyList<DateTimeFormatRule> ColumnFormatRules => ColumnFormatRuleList;

			public IReadOnlyList<DateTimeFormatRule> DirectoryFormatRules => DirectoryFormatRuleList;
		}

		private sealed class FakeVolumePathProvider : IVolumePathProvider
		{
			private readonly Dictionary<string, (string VolumeId, string RelativePath)> _volumes = new(StringComparer.OrdinalIgnoreCase);

			public void AddVolume(string path, string volumeId, string relativePath)
				=> _volumes[PortableDirectoryKey.NormalizePath(path)] = (volumeId, relativePath);

			public bool TryGetVolumeIdentity(string directoryPath, out string volumeId, out string relativePath)
			{
				if (_volumes.TryGetValue(PortableDirectoryKey.NormalizePath(directoryPath), out var volume))
				{
					volumeId = volume.VolumeId;
					relativePath = volume.RelativePath;
					return true;
				}

				volumeId = string.Empty;
				relativePath = string.Empty;
				return false;
			}
		}
	}
}
