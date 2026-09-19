using System.Xml.Linq;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>
/// Reads one of this module's resx triples as XML and hands back the keys and the sentences, one
/// collection per locale.
/// </summary>
/// <remarks>
/// <para>
/// It reads the FILES rather than asking a localizer, and the reason is the sweep's actual question:
/// "give me every value in every locale". A <see cref="System.Resources.ResourceManager"/> resolves
/// ONE culture at a time and falls back to the neutral text whenever a satellite assembly is missing,
/// so a filesystem path pasted into <c>DisplayNames.ru.resx</c> would be swept as its English fallback
/// and pass. Reading the three files means drift is caught in the file the author edited.
/// </para>
/// <para>
/// A missing file is a failed LOAD, never an empty locale: an empty locale is precisely how this sweep
/// would go blind, and returning one would let the caller's non-empty guard fire on a real absence
/// rather than silently sweeping nothing.
/// </para>
/// </remarks>
public static class ResourceValues
{
    /// <summary>The culture suffixes of a triple, neutral first.</summary>
    private static readonly string[] Suffixes = ["", ".ru", ".hy"];

    /// <summary>Every value of one resource family, grouped by locale: en, ru, hy.</summary>
    /// <param name="family">The family's base name, such as <c>DisplayNames</c>.</param>
    /// <returns>Three collections of <c>&lt;value&gt;</c> texts, in document order.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> ValuesByLocale(string family)
    {
        return Read(family, element => { return element.Element("value")?.Value ?? string.Empty; });
    }

    /// <summary>Every key of one resource family, grouped by locale: en, ru, hy.</summary>
    /// <param name="family">The family's base name, such as <c>DisplayNames</c>.</param>
    /// <returns>Three collections of <c>name</c> attributes, in the same document order.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> KeysByLocale(string family)
    {
        return Read(family, element => { return element.Attribute("name")?.Value ?? string.Empty; });
    }

    /// <summary>Reads one projection of every <c>&lt;data&gt;</c> element of a triple.</summary>
    /// <param name="family">The family's base name.</param>
    /// <param name="projection">What to take from each element.</param>
    /// <returns>Three collections, in the order neutral, ru, hy.</returns>
    private static List<IReadOnlyList<string>> Read(string family, Func<XElement, string> projection)
    {
        var folder = ResourcesFolder();

        return Suffixes
            .Select(suffix =>
            {
                var path = Path.Combine(folder, $"{family}{suffix}.resx");
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("A locale's resource file is missing, so it cannot be swept.", path);
                }

                return (IReadOnlyList<string>)XDocument.Load(path).Root!.Elements("data")
                    .Select(projection)
                    .ToList();
            })
            .ToList();
    }

    /// <summary>Locates the module's <c>Resources/</c> folder from the test assembly's location.</summary>
    /// <returns>Absolute path of <c>backend/src/Maran.Modules/Databases/Resources</c>.</returns>
    private static string ResourcesFolder()
    {
        return Path.Combine(BackendRoot(), "src", "Maran.Modules", "Databases", "Resources");
    }

    /// <summary>Walks up from the test binary to the folder holding <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the backend root.</returns>
    private static string BackendRoot()
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
