using System.Globalization;
using System.Xml.Linq;

namespace Maran.ArchitectureTests;

/// <summary>
/// Enforces rules/csharp.md "Each file is a triple … identical key sets, verified by a test".
/// A key present in the neutral file but missing from <c>.ru</c> or <c>.hy</c> does not fail the
/// build: <see cref="System.Resources.ResourceManager"/> silently falls back to the neutral text,
/// so the first person to notice is a Russian- or Armenian-speaking customer reading English. This
/// suite is that noticing, moved to CI — it reads the <c>.resx</c> files as the source of truth
/// rather than the compiled satellite assemblies, so drift is caught in the file the author edited.
/// </summary>
public sealed class ResourceKeyParityTests
{
    /// <summary>The translated cultures every neutral resource file must have a sibling for.</summary>
    private static readonly string[] TranslatedCultures = ["ru", "hy"];

    /// <summary>Every resx family carries the same keys in english russian and armenian.</summary>
    [Fact]
    public void Every_resx_family_carries_the_same_keys_in_english_russian_and_armenian()
    {
        var families = FindResourceFamilies();
        var problems = new List<string>();

        foreach (var (neutralPath, neutralKeys) in families)
        {
            foreach (var culture in TranslatedCultures)
            {
                var translatedPath = TranslatedPathFor(neutralPath, culture);
                if (!File.Exists(translatedPath))
                {
                    problems.Add($"{translatedPath} is missing; every resx is a triple (.resx/.ru.resx/.hy.resx).");
                    continue;
                }

                var translatedKeys = ReadKeys(translatedPath);

                var missing = neutralKeys.Except(translatedKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                foreach (var key in missing)
                {
                    problems.Add($"{translatedPath} is missing key '{key}' present in {Path.GetFileName(neutralPath)}.");
                }

                var extra = translatedKeys.Except(neutralKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                foreach (var key in extra)
                {
                    problems.Add($"{translatedPath} defines key '{key}' that {Path.GetFileName(neutralPath)} does not.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "Resource key sets have drifted apart:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>Resource parity runs against the resource files that actually exist.</summary>
    [Fact]
    public void Resource_parity_runs_against_the_resource_files_that_actually_exist()
    {
        // Guards the suite above from passing vacuously: a broken path or a moved backend folder
        // would find nothing to compare and report success (rules/testing.md "'No tests found' is a
        // FAILURE"). The count is not pinned to an exact number, which would make every new
        // resource file a failing test for no reason.
        Assert.NotEmpty(FindResourceFamilies());
    }

    /// <summary>Locates every neutral <c>.resx</c> under <c>backend/src</c> and reads its key set.</summary>
    /// <returns>One entry per family: the neutral file's path and the keys it declares.</returns>
    private static List<(string NeutralPath, HashSet<string> Keys)> FindResourceFamilies()
    {
        var sourceRoot = Path.Combine(FindBackendRoot(), "src");
        return Directory.EnumerateFiles(sourceRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path =>
            {
                return !IsBuildOutput(path);
            })
            .Where(path =>
            {
                return !HasCultureSuffix(path);
            })
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                return (path, ReadKeys(path));
            })
            .ToList();
    }

    /// <summary>Tells a generated copy under <c>obj/</c> or <c>bin/</c> from an authored source file.</summary>
    /// <param name="path">Absolute path of a candidate resource file.</param>
    private static bool IsBuildOutput(string path)
    {
        var relative = path.AsSpan();
        return relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Tells a translated file (<c>ErrorMessages.ru.resx</c>) from the neutral one
    /// (<c>ErrorMessages.resx</c>) by the second extension its name carries.
    /// </summary>
    /// <param name="path">Absolute path of a resource file.</param>
    private static bool HasCultureSuffix(string path)
    {
        var withoutResx = Path.GetFileNameWithoutExtension(path);
        var suffix = Path.GetExtension(withoutResx);
        return suffix.Length > 0;
    }

    /// <summary>Builds the sibling path of a neutral resource file for one culture.</summary>
    /// <param name="neutralPath">Absolute path of the neutral <c>.resx</c>.</param>
    /// <param name="culture">The culture code, e.g. <c>ru</c>.</param>
    /// <returns>The absolute path the translated file must occupy.</returns>
    private static string TranslatedPathFor(string neutralPath, string culture)
    {
        var directory = Path.GetDirectoryName(neutralPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(neutralPath);
        return Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{baseName}.{culture}.resx"));
    }

    /// <summary>Reads the <c>name</c> attribute of every <c>&lt;data&gt;</c> entry in a resource file.</summary>
    /// <param name="path">Absolute path of the resource file to read.</param>
    /// <returns>The keys it declares.</returns>
    private static HashSet<string> ReadKeys(string path)
    {
        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element =>
            {
                return element.Attribute("name")?.Value;
            })
            .Where(name =>
            {
                return !string.IsNullOrEmpty(name);
            })
            .Select(name =>
            {
                return name!;
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Walks up from the test binary to the folder holding <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the backend root.</returns>
    private static string FindBackendRoot()
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
}
