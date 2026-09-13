using System.Xml.Linq;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// Reads the module's three <c>ErrorMessages</c> resx files as XML and hands back the sentences a
/// customer would read, one collection per locale.
/// </summary>
/// <remarks>
/// <para>
/// It reads the FILES rather than asking the generated <c>ErrorMessages</c> class, and the reason is
/// the sweep's actual question: "give me every value in every locale". The generated class resolves
/// ONE culture at a time through a <c>ResourceManager</c>, and a <c>ResourceManager</c> falls back
/// to the neutral text whenever a satellite assembly is missing — so a filesystem path pasted into
/// <c>ErrorMessages.ru.resx</c> would be swept as its English fallback and pass. Reading the three
/// files means drift is caught in the file the author edited, which is the same choice the panel's
/// own <c>ResourceKeyParityTests</c> made.
/// </para>
/// <para>
/// A missing file is a failed LOAD, never an empty locale. Three collections come back or an
/// exception does: an empty locale is precisely the way this sweep would go blind, and returning one
/// would let the caller's <c>NotEmpty</c> guard fire on a real absence rather than silently sweeping
/// nothing.
/// </para>
/// </remarks>
public static class ErrorMessageValues
{
    /// <summary>The three resx files the module ships, neutral first.</summary>
    private static readonly string[] FileNames =
    [
        "ErrorMessages.resx",
        "ErrorMessages.ru.resx",
        "ErrorMessages.hy.resx",
    ];

    /// <summary>Every error sentence the module ships, grouped by locale: en, ru, hy.</summary>
    /// <returns>Three collections of <c>&lt;value&gt;</c> texts, in the order the files are listed.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> ByLocale()
    {
        var folder = ResourcesFolder();

        return FileNames
            .Select(fileName =>
            {
                return ValuesIn(Path.Combine(folder, fileName));
            })
            .ToList();
    }

    /// <summary>Every error KEY the module declares, grouped by locale: en, ru, hy.</summary>
    /// <returns>Three collections of <c>&lt;data name&gt;</c> attributes, in the order the files are listed.</returns>
    /// <remarks>
    /// The keys, not the sentences, because a code the module raises with no entry in one locale is
    /// a customer reading a raw identifier in that language and nothing else going wrong.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<string>> KeysByLocale()
    {
        var folder = ResourcesFolder();

        return FileNames
            .Select(fileName =>
            {
                return KeysIn(Path.Combine(folder, fileName));
            })
            .ToList();
    }

    /// <summary>Every value in one resx file.</summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <returns>The text of every <c>&lt;data&gt;</c> element's <c>&lt;value&gt;</c>.</returns>
    /// <exception cref="FileNotFoundException">The file is not there, which is a failure and not an empty locale.</exception>
    private static List<string> ValuesIn(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A locale's resource file is missing, so it cannot be swept.", path);
        }

        return XDocument.Load(path).Root!.Elements("data")
            .Select(element =>
            {
                return element.Element("value")?.Value ?? string.Empty;
            })
            .ToList();
    }

    /// <summary>Every key in one resx file.</summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <returns>The <c>name</c> attribute of every <c>&lt;data&gt;</c> element.</returns>
    /// <exception cref="FileNotFoundException">The file is not there, which is a failure and not an empty locale.</exception>
    private static List<string> KeysIn(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A locale's resource file is missing, so it cannot be swept.", path);
        }

        return XDocument.Load(path).Root!.Elements("data")
            .Select(element =>
            {
                return element.Attribute("name")?.Value ?? string.Empty;
            })
            .ToList();
    }

    /// <summary>Locates the module's <c>Resources/</c> folder from the test assembly's location.</summary>
    /// <returns>Absolute path of <c>backend/src/Maran.Modules/Ftp/Resources</c>.</returns>
    private static string ResourcesFolder()
    {
        return Path.Combine(BackendRoot(), "src", "Maran.Modules", "Ftp", "Resources");
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
