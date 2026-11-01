// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;
using System.Text.RegularExpressions;

namespace Files.App.Utils.Rename
{
	/// <summary>
	/// One item to rename, detached from any UI or storage type.
	/// </summary>
	public readonly record struct RenameTarget(
		string FullPath,
		string Name,
		bool IsFolder,
		DateTimeOffset CreatedUtc,
		DateTimeOffset ModifiedUtc,
		string ParentFolderName);

	/// <summary>
	/// Whether a row was renamed, left alone, or blocked by a rule-level problem.
	/// </summary>
	public enum RenameOutcomeKind
	{
		Unchanged,
		Changed,
		RuleError,
	}

	/// <summary>
	/// The rule-level problem that prevented a rename.
	/// </summary>
	public enum RenameError
	{
		None,
		BadPattern,
		RegexTimeout,
		InvalidName,
	}

	/// <summary>
	/// Result of applying a <see cref="RenameRule"/> to one <see cref="RenameTarget"/>.
	/// </summary>
	public sealed record RenameOutcome(string OriginalName, string NewName, RenameOutcomeKind Kind, RenameError Error);

	/// <summary>
	/// Pure batch rename engine: computes new names for a list of targets without touching the file system.
	/// </summary>
	/// <remarks>
	/// Pipeline per row: split the name by <see cref="RenameApplyTo"/>, apply the template to the target
	/// segment only, expand variables in the injected text (replacement/prefix/suffix), apply the case
	/// conversion to the name part, then validate the whole name. Counters advance once per matched row,
	/// never for rows that stay unchanged. In regex mode the replacement string is used verbatim as a
	/// substitution (so '$1' works) and therefore does not expand variables.
	/// </remarks>
	public sealed class RenameEngine
	{
		private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

		/// <summary>
		/// Computes the new name for every target under the given rule.
		/// </summary>
		public IReadOnlyList<RenameOutcome> Run(IReadOnlyList<RenameTarget> targets, RenameRule rule)
		{
			ArgumentNullException.ThrowIfNull(targets);
			ArgumentNullException.ThrowIfNull(rule);

			// Parse the find pattern once per run; a parse failure fails every row as BadPattern.
			Regex? findRegex = null;
			RenameError patternError = RenameError.None;
			if (rule.Template == RenameTemplate.FindReplace && rule.Find.Length > 0)
			{
				try
				{
					findRegex = new Regex(BuildFindPattern(rule), BuildRegexOptions(rule), MatchTimeout);
				}
				catch (ArgumentException)
				{
					patternError = RenameError.BadPattern;
				}
			}

			// One expander per run keeps counters continuous across rows; only matched rows advance them.
			string injectedPattern = rule.Template switch
			{
				RenameTemplate.Prepend => rule.Prefix,
				RenameTemplate.Append => rule.Suffix,
				_ => rule.ReplaceWith,
			};
			RenameVariableExpander expander = RenameVariableExpander.Create(injectedPattern);

			List<RenameOutcome> outcomes = new(targets.Count);
			foreach (RenameTarget target in targets)
				outcomes.Add(RenameOne(target, rule, findRegex, patternError, expander));

			return outcomes;
		}

		private static RenameOutcome RenameOne(in RenameTarget target, RenameRule rule, Regex? findRegex, RenameError patternError, RenameVariableExpander expander)
		{
			if (patternError != RenameError.None)
				return new RenameOutcome(target.Name, target.Name, RenameOutcomeKind.RuleError, patternError);

			try
			{
				var context = new VariableContext(target.CreatedUtc, target.ModifiedUtc, rule.DateSource, target.ParentFolderName);
				(string BaseName, string Extension) original = SplitName(target.Name);

				string segment = rule.ApplyTo switch
				{
					RenameApplyTo.ExtensionOnly => original.Extension,
					RenameApplyTo.NameAndExtension => target.Name,
					_ => original.BaseName,
				};

				string newBaseName = original.BaseName;
				string newExtension = original.Extension;
				string trailingText = string.Empty;
				bool matched = false;

				switch (rule.Template)
				{
					case RenameTemplate.Prepend:
						if (rule.Prefix.Length > 0 && !(rule.SkipItemsWithPrefix && segment.StartsWith(rule.Prefix, StringComparison.Ordinal)))
						{
							(newBaseName, newExtension) = ApplyToSegment(expander.Expand(context) + segment, rule.ApplyTo, original.BaseName, original.Extension);
							matched = true;
						}
						break;

					case RenameTemplate.Append:
						if (rule.Suffix.Length > 0)
						{
							string suffix = expander.Expand(context);
							if (rule.InsertBeforeExtension)
								newBaseName = original.BaseName + suffix;
							else
								trailingText = suffix;
							matched = true;
						}
						break;

					default:
						if (findRegex is not null && findRegex.IsMatch(segment))
						{
							string replacement = rule.Mode == RenameMode.Regex
								? rule.ReplaceWith
								: BuildSubstitution(expander.Expand(context), rule.Mode);
							(newBaseName, newExtension) = ApplyToSegment(findRegex.Replace(segment, replacement, rule.MatchAllOccurrences ? -1 : 1), rule.ApplyTo, original.BaseName, original.Extension);
							matched = true;
						}
						break;
				}

				if (!matched)
					return new RenameOutcome(target.Name, target.Name, RenameOutcomeKind.Unchanged, RenameError.None);

				string candidate = AssembleName(newBaseName, newExtension) + trailingText;

				// The case conversion applies to the name part only; the extension keeps its casing.
				(string candidateBaseName, string candidateExtension) = SplitName(candidate);
				candidate = AssembleName(ConvertCase(candidateBaseName, rule.CaseConversion), candidateExtension);

				if (string.Equals(candidate, target.Name, StringComparison.Ordinal))
					return new RenameOutcome(target.Name, target.Name, RenameOutcomeKind.Unchanged, RenameError.None);

				if (RenameValidator.Validate(candidate) is not null)
					return new RenameOutcome(target.Name, candidate, RenameOutcomeKind.RuleError, RenameError.InvalidName);

				return new RenameOutcome(target.Name, candidate, RenameOutcomeKind.Changed, RenameError.None);
			}
			catch (RegexMatchTimeoutException)
			{
				return new RenameOutcome(target.Name, target.Name, RenameOutcomeKind.RuleError, RenameError.RegexTimeout);
			}
			catch (ArgumentException)
			{
				return new RenameOutcome(target.Name, target.Name, RenameOutcomeKind.RuleError, RenameError.BadPattern);
			}
		}

		// Text mode escapes the find term; wildcard mode converts it and anchors it to the whole segment
		// because a Win32 expression describes a complete name; regex mode uses the term as written.
		private static string BuildFindPattern(RenameRule rule)
		{
			return rule.Mode switch
			{
				RenameMode.Wildcard => "^" + WildcardConverter.ToRegexPattern(rule.Find) + "$",
				RenameMode.Regex => rule.Find,
				_ => Regex.Escape(rule.Find),
			};
		}

		private static RegexOptions BuildRegexOptions(RenameRule rule)
		{
			var options = RegexOptions.CultureInvariant;
			if (!rule.CaseSensitive)
				options |= RegexOptions.IgnoreCase;

			return options;
		}

		// Makes the rendered replacement safe as a substitution string: '*' refers to the star number of
		// the find pattern in wildcard mode, and every other '$' stays literal.
		private static string BuildSubstitution(string renderedReplacement, RenameMode mode)
		{
			StringBuilder builder = new();
			int group = 1;

			foreach (char c in renderedReplacement)
			{
				if (c == '*' && mode == RenameMode.Wildcard)
					builder.Append("${").Append(group++).Append('}');
				else if (c == '$')
					builder.Append("$$");
				else
					builder.Append(c);
			}

			return builder.ToString();
		}

		private static (string BaseName, string Extension) ApplyToSegment(string segment, RenameApplyTo applyTo, string baseName, string extension)
		{
			return applyTo switch
			{
				RenameApplyTo.ExtensionOnly => (baseName, segment),
				RenameApplyTo.NameAndExtension => SplitName(segment),
				_ => (segment, extension),
			};
		}

		// Splits at the last dot; a name that is only an extension (".gitignore") stays whole.
		private static (string BaseName, string Extension) SplitName(string name)
		{
			int dotIndex = name.LastIndexOf('.');
			if (dotIndex <= 0)
				return (name, string.Empty);

			return (name[..dotIndex], name[(dotIndex + 1)..]);
		}

		private static string AssembleName(string baseName, string extension)
		{
			return extension.Length > 0 ? baseName + "." + extension : baseName;
		}

		private static string ConvertCase(string text, RenameCaseConversion conversion)
		{
			switch (conversion)
			{
				case RenameCaseConversion.Lowercase:
					return text.ToLowerInvariant();

				case RenameCaseConversion.Uppercase:
					return text.ToUpperInvariant();

				case RenameCaseConversion.FirstLetter:
				case RenameCaseConversion.EveryWord:
					bool everyWord = conversion == RenameCaseConversion.EveryWord;
					StringBuilder builder = new(text.Length);
					for (int i = 0; i < text.Length; i++)
					{
						bool uppercase = i == 0 || (everyWord && char.IsWhiteSpace(text[i - 1]));
						builder.Append(uppercase ? char.ToUpperInvariant(text[i]) : text[i]);
					}

					return builder.ToString();

				default:
					return text;
			}
		}
	}
}
