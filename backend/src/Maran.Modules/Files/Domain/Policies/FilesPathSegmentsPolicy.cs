namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// Takes apart the relative paths a mutation names. Pure text over values the validators have already
/// accepted: it decides nothing about containment, which is the agent's descriptor walk
/// (rules/security.md item 2). Joining a folder and a name is <c>FileEntryMapper.PathOf</c>.
/// </summary>
public static class FilesPathSegmentsPolicy
{
    /// <summary>The last segment of a path.</summary>
    /// <param name="path">A non-empty relative path.</param>
    /// <returns>The entry's own name.</returns>
    public static string NameOf(string path)
    {
        var cut = path.LastIndexOf(FilesPathPolicy.Separator);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// <summary>The folder a path lives in.</summary>
    /// <param name="path">A non-empty relative path.</param>
    /// <returns>The path without its last segment; empty when the entry is directly in the home.</returns>
    public static string ParentOf(string path)
    {
        var cut = path.LastIndexOf(FilesPathPolicy.Separator);
        return cut < 0 ? string.Empty : path[..cut];
    }
}
