namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The closed set of PHP extension names an administrator may switch, in the order the panel shows
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the panel's copy; the agent's catalogue decides.</b> The agent holds the authoritative
/// closed list and re-validates every name it is sent (rules/security.md item 12), so a name accepted
/// here and refused there is refused. The panel keeps its own copy so a typed or replayed request is
/// turned away before a task is begun, and so the list of labels the module owns can be checked
/// against it. When the agent's catalogue changes, this list changes in the same release.
/// </para>
/// <para>
/// A name here is a catalogue name (<c>mysql</c>, <c>gd</c>), never a package name: the agent derives
/// the package itself and never takes one from a caller.
/// </para>
/// </remarks>
public static class PhpExtensionCatalogue
{
    /// <summary>The catalogue names, alphabetical.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "bcmath",
        "curl",
        "gd",
        "gmp",
        "imagick",
        "intl",
        "mbstring",
        "mysql",
        "opcache",
        "redis",
        "soap",
        "xml",
        "zip",
    ];

    /// <summary>Says whether a name is one of the catalogue's.</summary>
    /// <param name="name">The candidate name, compared exactly: the catalogue is lowercase.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is in the catalogue.</returns>
    public static bool IsKnown(string? name)
    {
        return name is not null && Names.Contains(name, StringComparer.Ordinal);
    }
}
