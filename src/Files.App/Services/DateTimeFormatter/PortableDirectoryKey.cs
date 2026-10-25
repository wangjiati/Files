// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;

namespace Files.App.Services.DateTimeFormatter
{
	/// <summary>
	/// Resolves the stable volume identity used for directory rules on removable volumes.
	/// </summary>
	public interface IVolumePathProvider
	{
		/// <summary>
		/// Tries to get the identity of the volume that contains <paramref name="directoryPath"/>.
		/// </summary>
		/// <param name="directoryPath">The directory to inspect.</param>
		/// <param name="volumeId">The volume identity, or an empty string when unavailable.</param>
		/// <param name="relativePath">The normalized path relative to the volume root, or an empty string when unavailable.</param>
		/// <returns><see langword="true"/> when the path is on a mounted removable volume.</returns>
		bool TryGetVolumeIdentity(string directoryPath, out string volumeId, out string relativePath);
	}

	/// <summary>
	/// Represents a normalized directory scope that stays valid when a removable volume changes its drive letter.
	/// </summary>
	public sealed record PortableDirectoryKey
	{
		private PortableDirectoryKey(string absolutePath, string? volumeId, string relativePath)
		{
			AbsolutePath = absolutePath;
			VolumeId = string.IsNullOrEmpty(volumeId) ? null : volumeId;
			RelativePath = relativePath;
		}

		/// <summary>
		/// Gets the normalized absolute path, used for fixed directories and for display.
		/// </summary>
		public string AbsolutePath { get; }

		/// <summary>
		/// Gets the volume identity when the scope is on a removable volume.
		/// </summary>
		public string? VolumeId { get; }

		/// <summary>
		/// Gets the normalized path relative to the volume root.
		/// </summary>
		public string RelativePath { get; }

		/// <summary>
		/// Gets a value indicating whether the scope is identified by a volume and a relative path.
		/// </summary>
		public bool IsVolumeScoped => VolumeId is not null;

		/// <summary>
		/// Creates a key from a directory path, using volume identity when the path is on a removable volume.
		/// </summary>
		public static PortableDirectoryKey FromPath(string path, IVolumePathProvider volumePathProvider)
		{
			var absolutePath = NormalizePath(path);

			if (!string.IsNullOrWhiteSpace(path) &&
				volumePathProvider.TryGetVolumeIdentity(path, out var volumeId, out var relativePath) &&
				!string.IsNullOrWhiteSpace(volumeId))
			{
				return new PortableDirectoryKey(absolutePath, volumeId, NormalizeRelativePath(relativePath));
			}

			return new PortableDirectoryKey(absolutePath, null, absolutePath);
		}

		/// <summary>
		/// Creates a key from a persisted rule, preserving the stored volume identity.
		/// </summary>
		public static PortableDirectoryKey FromRule(DateTimeFormatRule rule)
		{
			ArgumentNullException.ThrowIfNull(rule);

			var absolutePath = NormalizePath(rule.DirectoryPath);

			if (!string.IsNullOrWhiteSpace(rule.VolumeId))
				return new PortableDirectoryKey(absolutePath, rule.VolumeId, NormalizeRelativePath(rule.RelativePath));

			return new PortableDirectoryKey(absolutePath, null, absolutePath);
		}

		/// <summary>
		/// Normalizes separators, duplicate separators, and trailing separators without touching the file system.
		/// </summary>
		public static string NormalizePath(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return string.Empty;

			var value = path.Replace('/', '\\');
			var isUnc = value.StartsWith(@"\\", StringComparison.Ordinal);
			var isRooted = value.StartsWith('\\');
			var segments = value.Split('\\', StringSplitOptions.RemoveEmptyEntries);

			if (segments.Length == 0)
				return isUnc ? @"\\" : @"\";

			var normalized = string.Join('\\', segments);

			if (isUnc)
				normalized = @"\\" + normalized;
			else if (isRooted)
				normalized = "\\" + normalized;

			// Keep the separator of a drive root such as "C:\".
			return normalized.Length == 2 && normalized[1] == ':' ? normalized + '\\' : normalized;
		}

		/// <summary>
		/// Normalizes a rule's directory scope and stores volume identity when the path is on a removable volume.
		/// Existing volume identity is retained when the volume is unavailable.
		/// </summary>
		public static DateTimeFormatRule NormalizeRule(DateTimeFormatRule rule, IVolumePathProvider volumePathProvider)
		{
			ArgumentNullException.ThrowIfNull(rule);

			if (string.IsNullOrWhiteSpace(rule.DirectoryPath))
				return rule;

			var key = FromPath(rule.DirectoryPath, volumePathProvider);

			if (key.IsVolumeScoped)
			{
				return rule with
				{
					DirectoryPath = key.AbsolutePath,
					VolumeId = key.VolumeId,
					RelativePath = key.RelativePath,
				};
			}

			return rule with { DirectoryPath = key.AbsolutePath };
		}

		/// <summary>
		/// Gets how many directory levels separate this scope from <paramref name="candidate"/>.
		/// </summary>
		/// <returns><see langword="false"/> when the scope does not contain the candidate directory.</returns>
		public bool TryGetDistance(PortableDirectoryKey candidate, bool includeSubdirectories, out int distance)
		{
			ArgumentNullException.ThrowIfNull(candidate);

			distance = 0;

			if (IsVolumeScoped != candidate.IsVolumeScoped)
				return false;

			string scopePath;
			string candidatePath;

			if (IsVolumeScoped)
			{
				if (!string.Equals(VolumeId, candidate.VolumeId, StringComparison.OrdinalIgnoreCase))
					return false;

				scopePath = RelativePath;
				candidatePath = candidate.RelativePath;
			}
			else
			{
				scopePath = AbsolutePath;
				candidatePath = candidate.AbsolutePath;
			}

			var scopeSegments = Split(scopePath);
			var candidateSegments = Split(candidatePath);

			if (candidateSegments.Length < scopeSegments.Length)
				return false;

			for (var i = 0; i < scopeSegments.Length; i++)
			{
				if (!string.Equals(scopeSegments[i], candidateSegments[i], StringComparison.OrdinalIgnoreCase))
					return false;
			}

			if (!includeSubdirectories && candidateSegments.Length != scopeSegments.Length)
				return false;

			distance = candidateSegments.Length - scopeSegments.Length;
			return true;
		}

		private static string NormalizeRelativePath(string? relativePath)
		{
			if (string.IsNullOrWhiteSpace(relativePath))
				return @"\";

			var normalized = NormalizePath(relativePath);

			if (normalized is @"\" or @"\\")
				return @"\";

			return normalized.StartsWith('\\') ? normalized : "\\" + normalized;
		}

		private static string[] Split(string path)
			=> path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>
	/// Uses the volume serial number of removable volumes so directory rules survive drive letter changes.
	/// </summary>
	internal sealed class WindowsVolumePathProvider : IVolumePathProvider
	{
		public bool TryGetVolumeIdentity(string directoryPath, out string volumeId, out string relativePath)
		{
			volumeId = string.Empty;
			relativePath = string.Empty;

			if (string.IsNullOrWhiteSpace(directoryPath))
				return false;

			string? root;

			try
			{
				root = SystemIO.Path.GetPathRoot(directoryPath);
			}
			catch (ArgumentException)
			{
				return false;
			}

			if (string.IsNullOrEmpty(root) || root.Length < 3)
				return false;

			SystemIO.DriveInfo drive;

			try
			{
				drive = new SystemIO.DriveInfo(root);

				if (!drive.IsReady || drive.DriveType is not SystemIO.DriveType.Removable)
					return false;
			}
			catch (Exception ex) when (ex is ArgumentException or SystemIO.IOException or UnauthorizedAccessException)
			{
				return false;
			}

			if (!WindowsStorableHelpers.TryGetVolumeSerialNumber(root, out var serialNumber))
				return false;

			volumeId = serialNumber.ToString("X8", CultureInfo.InvariantCulture);

			var normalizedPath = PortableDirectoryKey.NormalizePath(directoryPath);
			var normalizedRoot = PortableDirectoryKey.NormalizePath(root);

			if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
				return false;

			relativePath = normalizedPath.Length > normalizedRoot.Length
				? @"\" + normalizedPath[normalizedRoot.Length..]
				: @"\";

			return true;
		}
	}
}
