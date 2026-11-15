// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// An item collected by <see cref="DirectoryRecursion.Flatten{T}"/> with its path relative to the
	/// node it descends from.
	/// </summary>
	public readonly record struct DirectoryRecursionEntry<T>(T Item, string RelativePath);

	/// <summary>
	/// Flattens a tree of already built nodes into depth-limited entries. It only collects; callers own
	/// all enumeration and IO.
	/// </summary>
	public static class DirectoryRecursion
	{
		public const int MaxDepth = 64;

		/// <summary>
		/// Flattens <paramref name="roots"/> (level 1) and their descendants up to <paramref name="maxDepth"/>.
		/// <paramref name="pathOf"/> returns a node's full path; the relative path is its last segment below
		/// the parent, joined with a backslash. A repeated path (a junction cycle) is skipped with its subtree.
		/// </summary>
		public static IReadOnlyList<DirectoryRecursionEntry<T>> Flatten<T>(
			IReadOnlyList<T> roots,
			Func<T, IReadOnlyList<T>?> childrenOf,
			Func<T, string> pathOf,
			int maxDepth)
		{
			ArgumentNullException.ThrowIfNull(roots);
			ArgumentNullException.ThrowIfNull(childrenOf);
			ArgumentNullException.ThrowIfNull(pathOf);

			var depth = Math.Clamp(maxDepth, 1, MaxDepth);
			var results = new List<DirectoryRecursionEntry<T>>();
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var stack = new Stack<(T Node, string ParentPath, int Level)>();

			// Reversed pushes keep the depth-first result in the input order.
			for (var index = roots.Count - 1; index >= 0; index--)
				stack.Push((roots[index], string.Empty, 1));

			while (stack.TryPop(out var entry))
			{
				var path = pathOf(entry.Node) ?? string.Empty;
				var name = GetName(path);
				var relativePath = entry.Level is 1
					? name
					: Combine(entry.ParentPath, name);

				// A node reachable twice, or through a path that repeats an ancestor, would loop forever.
				if (!visited.Add(string.IsNullOrEmpty(path) ? relativePath : path))
					continue;

				results.Add(new(entry.Node, relativePath));

				if (entry.Level >= depth || childrenOf(entry.Node) is not { } children)
					continue;

				for (var index = children.Count - 1; index >= 0; index--)
					stack.Push((children[index], relativePath, entry.Level + 1));
			}

			return results;
		}

		private static string Combine(string parentPath, string name)
			=> parentPath.Length is 0 ? name : parentPath + "\\" + name;

		private static string GetName(string path)
		{
			var trimmed = path.TrimEnd('\\', '/');
			var separator = trimmed.LastIndexOfAny(['\\', '/']);

			return separator < 0 ? trimmed : trimmed[(separator + 1)..];
		}
	}
}
