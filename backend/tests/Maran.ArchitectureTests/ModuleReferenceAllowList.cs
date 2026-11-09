namespace Maran.ArchitectureTests;

/// <summary>
/// The packages and frameworks a packageable module's project file may reference, read from
/// <c>ModuleReferenceAllowList.txt</c> beside this class's source.
/// </summary>
/// <remarks>
/// A file in the test project and not a list in code, so that widening what a downloaded module may
/// bring into the panel process is a diff in a file whose only job is that, with a reason on every
/// line. The file is read from the source tree rather than copied to the output, so editing it needs
/// no project change.
/// </remarks>
public sealed class ModuleReferenceAllowList
{
    /// <summary>The kind word of a package line.</summary>
    private const string PackageKind = "package";

    /// <summary>The kind word of a framework line.</summary>
    private const string FrameworkKind = "framework";

    /// <summary>Gets the allowed package ids.</summary>
    public IReadOnlySet<string> Packages { get; }

    /// <summary>Gets the allowed framework references.</summary>
    public IReadOnlySet<string> Frameworks { get; }

    /// <summary>Creates a list from already-parsed entries.</summary>
    /// <param name="packages">The allowed package ids.</param>
    /// <param name="frameworks">The allowed framework references.</param>
    public ModuleReferenceAllowList(IReadOnlySet<string> packages, IReadOnlySet<string> frameworks)
    {
        Packages = packages;
        Frameworks = frameworks;
    }

    /// <summary>Reads the allow-list file that sits in the architecture test project.</summary>
    /// <returns>The parsed list.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which would otherwise allow nothing silently.</exception>
    public static ModuleReferenceAllowList Load()
    {
        var path = Path.Combine(
            ResourceTriples.BackendRoot(),
            "tests",
            "Maran.ArchitectureTests",
            "ModuleReferenceAllowList.txt");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The module reference allow-list is missing.", path);
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses allow-list text.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The parsed list.</returns>
    /// <exception cref="FormatException">A line is not <c>kind|name|reason</c> with all three present.</exception>
    public static ModuleReferenceAllowList Parse(string text)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        var frameworks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', 3);
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new FormatException($"Allow-list line '{line}' is not 'kind|name|reason' with a reason.");
            }

            var target = parts[0].Trim() switch
            {
                PackageKind => packages,
                FrameworkKind => frameworks,
                _ => throw new FormatException($"Allow-list line '{line}' has an unknown kind '{parts[0]}'."),
            };
            target.Add(parts[1].Trim());
        }

        return new ModuleReferenceAllowList(packages, frameworks);
    }
}
