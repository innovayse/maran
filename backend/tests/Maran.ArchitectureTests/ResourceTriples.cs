using System.Globalization;
using System.Xml.Linq;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads the authored <c>.resx</c> triples under <c>backend/src</c>, so a law can judge the VALUE a
/// customer would read and not merely the key an author declared.
/// </summary>
/// <remarks>
/// <para>
/// The files rather than the compiled satellite assemblies, for the reason
/// <see cref="ResourceKeyParityTests"/> gives: drift is then caught in the file the author edited.
/// The files rather than a resolved <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
/// for a second reason — a missing translated entry does NOT report itself through the localizer,
/// it silently answers with the neutral English text, so a law asking the localizer "is there a
/// Russian name" is answered "yes" by a build that has none.
/// </para>
/// <para>
/// UNOBSERVED HERE: this type reads what is written down. It cannot tell a correct translation from
/// a fluent wrong one, and it knows nothing about which resource pool a given key is resolved
/// against — the caller names the family it means.
/// </para>
/// </remarks>
public static class ResourceTriples
{
    /// <summary>The cultures every neutral resource file must have a sibling for.</summary>
    public static readonly string[] TranslatedCultures = ["ru", "hy"];

    /// <summary>Every authored neutral <c>.resx</c> under <c>backend/src</c>.</summary>
    /// <returns>Absolute paths, ordered, with build output and culture-suffixed files excluded.</returns>
    public static IReadOnlyList<string> NeutralPaths()
    {
        var sourceRoot = Path.Combine(BackendRoot(), "src");
        return Directory.EnumerateFiles(sourceRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path =>
            {
                return !IsBuildOutput(path) && !HasCultureSuffix(path);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Locates one authored resource family by the folder and base name it lives under.</summary>
    /// <param name="relativePath">
    /// The neutral file's path relative to <c>backend/src</c>, using forward slashes.
    /// </param>
    /// <returns>The absolute path of the neutral file.</returns>
    /// <exception cref="FileNotFoundException">The family named does not exist.</exception>
    public static string NeutralPathOf(string relativePath)
    {
        var absolute = Path.Combine(BackendRoot(), "src", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"No authored resource file at {relativePath}; a law is about to report on nothing.",
                absolute);
        }

        return absolute;
    }

    /// <summary>The sibling path a neutral resource file's translation occupies.</summary>
    /// <param name="neutralPath">Absolute path of the neutral <c>.resx</c>.</param>
    /// <param name="culture">The culture code, e.g. <c>ru</c>.</param>
    /// <returns>The absolute path the translated file must occupy.</returns>
    public static string TranslatedPathOf(string neutralPath, string culture)
    {
        var directory = Path.GetDirectoryName(neutralPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(neutralPath);
        return Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{baseName}.{culture}.resx"));
    }

    /// <summary>Reads one resource file's entries.</summary>
    /// <param name="path">Absolute path of the file; a file that does not exist reads as empty.</param>
    /// <returns>Key to value, as authored.</returns>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in XDocument.Load(path).Root!.Elements("data"))
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            entries[name] = element.Element("value")?.Value ?? string.Empty;
        }

        return entries;
    }

    /// <summary>Reads every authored file's entries into one map, the way the shared pool merges them.</summary>
    /// <param name="culture">The culture code, or <c>null</c> for the neutral files.</param>
    /// <returns>Key to the values declared for it, so a caller can see a collision rather than lose one.</returns>
    /// <remarks>
    /// A list per key rather than one value, because every module's resources feed ONE pool
    /// (<see cref="ManifestUniquenessTests"/>) and a merge that let the last file win would hide the
    /// collision that pool makes possible.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadAll(string? culture)
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var neutralPath in NeutralPaths())
        {
            var path = culture is null ? neutralPath : TranslatedPathOf(neutralPath, culture);
            foreach (var (key, value) in Read(path))
            {
                if (!merged.TryGetValue(key, out var values))
                {
                    values = [];
                    merged[key] = values;
                }

                values.Add(value);
            }
        }

        return merged.ToDictionary(
            entry =>
            {
                return entry.Key;
            },
            entry =>
            {
                return (IReadOnlyList<string>)entry.Value;
            },
            StringComparer.Ordinal);
    }

    /// <summary>Walks up from the test binary to the folder holding <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the backend root.</returns>
    public static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Maran.sln not found above the test output directory.");
    }

    /// <summary>Tells a generated copy under <c>obj/</c> or <c>bin/</c> from an authored source file.</summary>
    /// <param name="path">Absolute path of a candidate resource file.</param>
    /// <returns><c>true</c> when the file is build output.</returns>
    private static bool IsBuildOutput(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>Tells a translated file from the neutral one by the second extension its name carries.</summary>
    /// <param name="path">Absolute path of a resource file.</param>
    /// <returns><c>true</c> when the name carries a culture suffix.</returns>
    private static bool HasCultureSuffix(string path)
    {
        var withoutResx = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(withoutResx).Length > 0;
    }
}
