using System.Text.RegularExpressions;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// What a name inside a package may be, judged by its text alone and the same way for an archive entry
/// and for a path the manifest lists. The rule is an allow-list (ASCII letters, digits, dot, hyphen and
/// underscore in forward-slash separated segments), so non-UTF-8 names, control characters, drive
/// letters, backslashes and every spelling of a traversal are refused without being enumerated.
/// </summary>
public static class IpmEntryNameRule
{
    /// <summary>The longest name, in characters.</summary>
    private const int MaxNameLength = 255;

    /// <summary>The most folders and the file name together a path may have.</summary>
    private const int MaxSegments = 16;

    /// <summary>One path segment: the allow-list above.</summary>
    private static readonly Regex Segment = new(
        "^[A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Refusal for a name that leaves the package folder or is not relative.</summary>
    private static readonly Error Traversal = Error.Of(nameof(ErrorMessages.IpmEntryTraversal), ErrorType.Validation);

    /// <summary>Refusal for a name that is simply not allowed.</summary>
    private static readonly Error Invalid = Error.Of(nameof(ErrorMessages.IpmEntryNameInvalid), ErrorType.Validation);

    /// <summary>Judges one name.</summary>
    /// <param name="name">The entry name or manifest path.</param>
    /// <returns>Null when the name is allowed; otherwise the refusal.</returns>
    public static Error? Judge(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return Invalid;
        }

        if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':'))
        {
            return Traversal;
        }

        var segments = name.Split('/');

        if (segments.Any(segment =>
            {
                return segment is ".." or ".";
            }))
        {
            return Traversal;
        }

        if (segments.Length > MaxSegments)
        {
            return Invalid;
        }

        return segments.All(segment =>
        {
            return Segment.IsMatch(segment);
        })
            ? null
            : Invalid;
    }
}
