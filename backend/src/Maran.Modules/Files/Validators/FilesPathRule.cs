using System.Globalization;
using System.Text;
using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Validators;

/// <summary>
/// What this panel accepts as a path inside an account's home. The ONE rule every route that takes a
/// path applies at the boundary.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the panel being a good citizen, not the control.</b> The control is the agent's
/// descriptor walk (rules/security.md item 2). This rule never parses a path to decide containment and
/// never touches the disk; it exists so a customer is told what is wrong with the path they typed
/// instead of being handed a refusal from a process they cannot see, and so an obviously hostile value
/// never costs an agent call. A reviewer must not accept "this rule stops traversal" as the answer to a
/// traversal finding.
/// </para>
/// <para>
/// A path is relative and written with <c>/</c>; the empty string is the home itself. Refused: a leading
/// <c>/</c>, any <c>\</c>, an empty segment (so a trailing or doubled <c>/</c>), a segment <c>.</c> or
/// <c>..</c>, any control or line-separating character, text that is not valid Unicode, a segment over
/// 255 bytes, more than 64 segments, and a whole path over 4096 bytes — measured in UTF-8 BYTES, as the
/// agent measures them. A look-alike of the slash (U+2215) is an ordinary character of a name to the
/// file system and is accepted as one.
/// </para>
/// </remarks>
public static class FilesPathRule
{
    /// <summary>Whether a candidate is a well-formed relative path; the empty string (the home) is.</summary>
    /// <param name="candidate">The path as received, already URL-decoded by the framework.</param>
    /// <returns>True when every rule above holds.</returns>
    public static bool IsWellFormed(string? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        if (candidate.Length == 0)
        {
            return true;
        }

        if (Encoding.UTF8.GetByteCount(candidate) > FilesPathPolicy.MaximumPathBytes)
        {
            return false;
        }

        var segments = candidate.Split(FilesPathPolicy.Separator);
        if (segments.Length > FilesPathPolicy.MaximumSegments)
        {
            return false;
        }

        return segments.All(IsWellFormedSegment);
    }

    /// <summary>Whether a candidate names an entry (it is well formed and is not the home).</summary>
    /// <param name="candidate">The path as received.</param>
    /// <returns>True for a well-formed, non-empty path.</returns>
    public static bool IsEntryPath(string? candidate)
    {
        return !string.IsNullOrEmpty(candidate) && IsWellFormed(candidate);
    }

    /// <summary>Whether a path points into the account's trash directory, which only the trash routes may reach.</summary>
    /// <param name="candidate">The path as received.</param>
    /// <returns>True when the first segment is the reserved trash name.</returns>
    public static bool IsReserved(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var separator = candidate.IndexOf(FilesPathPolicy.Separator, StringComparison.Ordinal);
        var first = separator < 0 ? candidate : candidate[..separator];
        return string.Equals(first, FilesPathPolicy.ReservedTrashName, StringComparison.Ordinal);
    }

    /// <summary>Whether one segment is acceptable on its own: the rule a path applies to each of its parts.</summary>
    /// <param name="segment">One name, with no <c>/</c> in it.</param>
    /// <returns>True when the segment is non-empty, is not <c>.</c> or <c>..</c>, is bounded and is clean text.</returns>
    public static bool IsWellFormedSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or "..")
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(segment) > FilesPathPolicy.MaximumSegmentBytes)
        {
            return false;
        }

        // A lone surrogate has no UTF-8 form, so the agent would read a different name than was typed.
        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            if (char.IsHighSurrogate(character) && index + 1 < segment.Length && char.IsLowSurrogate(segment[index + 1]))
            {
                index++;
                continue;
            }

            if (char.IsSurrogate(character) || char.IsControl(character) || character == '\\' || IsLineSeparating(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a character is a Unicode line or paragraph separator, which is not a control character.</summary>
    /// <param name="character">The character.</param>
    /// <returns>True for U+2028 and U+2029.</returns>
    private static bool IsLineSeparating(char character)
    {
        var category = char.GetUnicodeCategory(character);
        return category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }
}
