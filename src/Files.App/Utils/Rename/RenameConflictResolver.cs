// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// What the conflict resolver decided to do with a row's proposed name.
	/// </summary>
	public enum ConflictStatus
	{
		None,
		WillAppendNumber,
		WillSkip,
		WillOverwrite,
	}

	/// <summary>
	/// One row to resolve, detached from any UI or storage type.
	/// </summary>
	/// <remarks>
	/// <see cref="Id"/> is an opaque token returned unchanged on <see cref="ConflictOutcome.Id"/>;
	/// callers choose their own convention.
	/// </remarks>
	public sealed record ConflictInput(
		string Id,
		string FolderPath,
		string OriginalName,
		string ProposedName,
		IReadOnlySet<string> ExistingNamesInFolder);

	/// <summary>
	/// The conflict resolver's decision for one row.
	/// </summary>
	/// <remarks>
	/// <see cref="FinalName"/> is the name the item ends up with: the proposed name when the row is
	/// applied or overwrites, the numbered name when a number was appended, the original name when skipped.
	/// </remarks>
	public sealed record ConflictOutcome(string Id, string FinalName, ConflictStatus Status);

	/// <summary>
	/// Pure conflict resolver: decides the final name of every row without touching the file system.
	/// </summary>
	/// <remarks>
	/// Names compare with <see cref="StringComparer.OrdinalIgnoreCase"/> and rows group by their
	/// <see cref="ConflictInput.FolderPath"/> the same way. A proposed name conflicts when it equals,
	/// ignoring case, a name already occupied in its folder: an existing file, or the final name of an
	/// earlier row. A row's own original name never conflicts with itself, which keeps case-only renames
	/// and identity proposals conflict-free. Under <see cref="ConflictStrategy.AppendNumber"/> the suffix
	/// " (n)" is inserted before the extension, starting at 2 and walking forward until the name is free.
	/// Skipped rows keep their original name; the name of every resolved row stays occupied.
	/// </remarks>
	public static class RenameConflictResolver
	{
		/// <summary>
		/// Resolves the final name of every row under the given strategy, in input order.
		/// </summary>
		public static IReadOnlyList<ConflictOutcome> Resolve(IReadOnlyList<ConflictInput> rows, ConflictStrategy strategy)
		{
			ArgumentNullException.ThrowIfNull(rows);

			// One occupied-name set per folder, seeded with the existing names every row reports for
			// that folder and grown with each resolved final name; comparisons are case-insensitive
			// regardless of the comparer the caller used for the sets.
			var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (ConflictInput row in rows)
			{
				if (!taken.TryGetValue(row.FolderPath, out var names))
					taken[row.FolderPath] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (string name in row.ExistingNamesInFolder)
					names.Add(name);
			}

			List<ConflictOutcome> outcomes = new(rows.Count);
			foreach (ConflictInput row in rows)
			{
				HashSet<string> names = taken[row.FolderPath];
				ConflictStatus status = ConflictStatus.None;
				string finalName = row.ProposedName;

				if (IsOccupied(names, row.OriginalName, row.ProposedName))
				{
					switch (strategy)
					{
						case ConflictStrategy.AppendNumber:
							finalName = AppendNumberUntilFree(names, row.OriginalName, row.ProposedName);
							status = ConflictStatus.WillAppendNumber;
							break;

						case ConflictStrategy.Skip:
							finalName = row.OriginalName;
							status = ConflictStatus.WillSkip;
							break;

						default:
							status = ConflictStatus.WillOverwrite;
							break;
					}
				}

				names.Add(finalName);
				outcomes.Add(new ConflictOutcome(row.Id, finalName, status));
			}

			return outcomes;
		}

		// A name is occupied when the folder already holds it, except for the row's own original name:
		// on Windows renaming to another casing of itself is not a collision.
		private static bool IsOccupied(HashSet<string> names, string originalName, string candidate)
		{
			return names.Contains(candidate) && !string.Equals(candidate, originalName, StringComparison.OrdinalIgnoreCase);
		}

		// Splits at the last dot like RenameEngine; a name that is only an extension stays whole.
		private static string AppendNumberUntilFree(HashSet<string> names, string originalName, string proposedName)
		{
			int dotIndex = proposedName.LastIndexOf('.');
			string baseName = dotIndex <= 0 ? proposedName : proposedName[..dotIndex];
			string extension = dotIndex <= 0 ? string.Empty : proposedName[(dotIndex + 1)..];

			for (int n = 2; ; n++)
			{
				string candidate = extension.Length > 0 ? $"{baseName} ({n}).{extension}" : $"{baseName} ({n})";
				if (!IsOccupied(names, originalName, candidate))
					return candidate;
			}
		}
	}
}
