namespace Maran.Modules.Files.Queries.ListFileEntries;

/// <summary>The names a folder listing or a search result may be sorted by, as the <c>sort</c> parameter spells them.</summary>
/// <remarks>
/// There is no <c>kind</c> field. The agent lists folders before every other entry whatever the order,
/// so a kind order would add nothing ascending and could not be served by the agent descending.
/// </remarks>
public static class FileEntrySortFields
{
    /// <summary>The entry's name, compared byte-wise; the default, ascending.</summary>
    public const string Name = "name";

    /// <summary>When the entry was last modified.</summary>
    public const string Modified = "modified";

    /// <summary>The entry's size in bytes.</summary>
    public const string Size = "size";
}
