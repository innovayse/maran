using System.Text.Json;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads the SPA's translation bundles so a claim that the interface owns a word can be checked
/// against the interface rather than believed.
/// </summary>
/// <remarks>
/// The authored JSON under <c>frontend/src/locales/</c> is the source of truth, not a build output:
/// drift has to be caught in the file the author edited, which is the same choice
/// <see cref="ResourceKeyParityTests"/> makes about the backend's <c>.resx</c> triples.
/// </remarks>
public static class SpaLocaleKeys
{
    /// <summary>The three languages every operator-facing word must exist in.</summary>
    public static readonly string[] Languages = ["en", "ru", "hy"];

    /// <summary>Every key one language's bundle defines, in dotted form.</summary>
    /// <param name="language">The language folder to read, e.g. <c>ru</c>.</param>
    /// <returns>The full key paths, such as <c>backups.status.running</c>.</returns>
    public static IReadOnlySet<string> KeysOf(string language)
    {
        var folder = Path.Combine(RepositoryRoot(), "frontend", "src", "locales", language);
        if (!Directory.Exists(folder))
        {
            throw new InvalidOperationException($"The SPA has no locale folder for '{language}' at {folder}.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Collect(document.RootElement, string.Empty, keys);
        }

        return keys;
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    /// <returns>Absolute path of the folder holding <c>backend/</c> and <c>frontend/</c>.</returns>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }

    /// <summary>Flattens one JSON object into dotted keys.</summary>
    /// <param name="element">The node being walked.</param>
    /// <param name="prefix">The dotted path of that node, empty at the root.</param>
    /// <param name="keys">The set every leaf's path is added to.</param>
    private static void Collect(JsonElement element, string prefix, HashSet<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            keys.Add(prefix);
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            Collect(property.Value, path, keys);
        }
    }
}
