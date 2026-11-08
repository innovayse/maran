using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Validators;

/// <summary>
/// What this panel accepts as the list of paths a batch names: each item goes through the same rule a
/// single path does, so a batch is never a way around it.
/// </summary>
/// <remarks>
/// The count ceiling is <c>FilesOptions.MaxBatchItems</c>, which only a validator holding the options
/// can apply; this rule judges the items.
/// </remarks>
public static class FilesPathListRule
{
    /// <summary>Whether every item of a batch names an entry by a well-formed path.</summary>
    /// <param name="candidates">The batch as received.</param>
    /// <returns>True for a non-null list whose every item is a well-formed, non-empty path.</returns>
    public static bool IsEveryEntryPath(IReadOnlyCollection<string>? candidates)
    {
        return candidates is not null && candidates.All(FilesPathRule.IsEntryPath);
    }

    /// <summary>Whether no item of a batch points into the trash directory.</summary>
    /// <param name="candidates">The batch as received.</param>
    /// <returns>True when no item has the reserved trash name as its first segment.</returns>
    public static bool IsNoneReserved(IReadOnlyCollection<string>? candidates)
    {
        return candidates is not null && !candidates.Any(FilesPathRule.IsReserved);
    }

    /// <summary>Whether a destination folder lies outside every source it receives.</summary>
    /// <param name="sources">The entries a move or a copy names.</param>
    /// <param name="destinationDirectory">The folder they land in, relative to the home.</param>
    /// <returns>
    /// True when the destination is neither a source nor beneath one. Compared on whole segments, so
    /// <c>ab</c> is not inside <c>a</c>. A source that is not a list is judged by its own rule, not this one.
    /// </returns>
    public static bool IsNoneContaining(IReadOnlyCollection<string>? sources, string? destinationDirectory)
    {
        if (sources is null || destinationDirectory is null)
        {
            return true;
        }

        return !sources.Any(source =>
        {
            return source.Length > 0
                && (destinationDirectory == source
                    || destinationDirectory.StartsWith(source + FilesPathPolicy.Separator, StringComparison.Ordinal));
        });
    }
}
