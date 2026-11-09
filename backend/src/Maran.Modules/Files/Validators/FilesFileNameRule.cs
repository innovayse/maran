using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Validators;

/// <summary>
/// What this panel accepts as ONE name: a rename's new name, or a name a customer gives an archive.
/// </summary>
/// <remarks>
/// One segment, so no <c>/</c>, and the same segment rule a path applies to each of its parts. The
/// reserved trash name is refused outright: this rule does not know which folder the name will land
/// in, and a name equal to the trash directory's is never one a customer needs.
/// </remarks>
public static class FilesFileNameRule
{
    /// <summary>Whether a candidate is one acceptable name.</summary>
    /// <param name="candidate">The name as typed.</param>
    /// <returns>True when it is a single well-formed segment that is not the reserved trash name.</returns>
    public static bool IsValid(string? candidate)
    {
        if (candidate is null || candidate.Contains(FilesPathPolicy.Separator, StringComparison.Ordinal))
        {
            return false;
        }

        return FilesPathRule.IsWellFormedSegment(candidate)
            && !string.Equals(candidate, FilesPathPolicy.ReservedTrashName, StringComparison.Ordinal);
    }
}
