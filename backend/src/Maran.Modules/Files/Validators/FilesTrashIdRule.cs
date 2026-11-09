using System.Text.RegularExpressions;

namespace Maran.Modules.Files.Validators;

/// <summary>
/// What the panel accepts as the identity of a trash entry: the form the agent mints, Unix seconds, a
/// dash and sixteen lower-case hexadecimal digits.
/// </summary>
/// <remarks>
/// The panel being a good citizen, not the control: the agent re-validates the id before it names a
/// directory with it (rules/security.md "Input"). The pattern is anchored and has no alternation, so it
/// cannot match a path separator, a dot segment or a control character.
/// </remarks>
public static partial class FilesTrashIdRule
{
    /// <summary>The longest an id can be: twelve digits of seconds, the dash and sixteen hex digits.</summary>
    public const int MaximumLength = 29;

    /// <summary>Whether <paramref name="candidate"/> has the shape of a trash id.</summary>
    /// <param name="candidate">The value to test; null is not an id.</param>
    /// <returns>True when the value is shaped like an id the agent minted.</returns>
    public static bool IsValid(string? candidate)
    {
        return candidate is not null && candidate.Length <= MaximumLength && Shape().IsMatch(candidate);
    }

    /// <summary>The anchored shape of an id.</summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex("^[0-9]{1,12}-[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
