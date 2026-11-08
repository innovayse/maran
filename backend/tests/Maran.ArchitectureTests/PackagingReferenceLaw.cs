using System.Xml.Linq;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rules <see cref="PackagingIsolationTests"/> enforces on <c>Maran.Packaging</c>: it references the
/// Sdk and the SharedKernel and nothing else of ours, and no package at all.
/// </summary>
/// <remarks>
/// <para>
/// The library is the one thing a module author, the tooling verb and the closed loader all run to decide
/// whether a package is acceptable. If it could reach the Host or a module, the answer could depend on
/// what happens to be composed, and it could not run offline. If it brought a package, that package would
/// be part of the verification and of its attack surface; the BCL is what it reads archives, hashes and
/// signatures with.
/// </para>
/// <para>
/// The project file is the declaration and the compiled assembly is the evidence, judged separately for
/// the same reason <see cref="ModuleReferenceLaw"/> does.
/// </para>
/// </remarks>
public static class PackagingReferenceLaw
{
    /// <summary>The rule: a project reference the library may not have.</summary>
    public const string ProjectReferenceNotAllowed = "ProjectReferenceNotAllowed";

    /// <summary>The rule: a package reference the library may not have.</summary>
    public const string PackageNotAllowed = "PackageNotAllowed";

    /// <summary>The rule: a product assembly the library is compiled against and may not be.</summary>
    public const string CompiledReferenceNotAllowed = "CompiledReferenceNotAllowed";

    /// <summary>The assembly name prefix every product assembly carries.</summary>
    private const string ProductPrefix = "Maran.";

    /// <summary>The library's own assembly name.</summary>
    private const string OwnAssembly = "Maran.Packaging";

    /// <summary>The two projects of ours the library may reference.</summary>
    private static readonly string[] AllowedProjects = ["Maran.Sdk", "Maran.SharedKernel"];

    /// <summary>Judges the library's project file.</summary>
    /// <param name="projectText">The text of the <c>.csproj</c>.</param>
    /// <returns>The violations, ordered; empty when the file obeys the rules.</returns>
    public static IReadOnlyList<string> ProjectViolations(string projectText)
    {
        var project = XDocument.Parse(projectText);
        var found = new List<string>();

        foreach (var name in References(project, "ProjectReference")
                     .Select(path => { return Path.GetFileNameWithoutExtension(path.Replace('\\', '/')); })
                     .Where(name => { return !AllowedProjects.Contains(name, StringComparer.Ordinal); }))
        {
            found.Add($"{ProjectReferenceNotAllowed}: Maran.Packaging references project {name}.");
        }

        foreach (var package in References(project, "PackageReference"))
        {
            found.Add($"{PackageNotAllowed}: Maran.Packaging references package {package}.");
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Judges the assemblies the library was compiled against.</summary>
    /// <param name="referencedAssemblyNames">The simple names of the referenced assemblies.</param>
    /// <returns>The violations, ordered; empty when only the Sdk and the SharedKernel are referenced of ours.</returns>
    public static IReadOnlyList<string> CompiledViolations(IEnumerable<string> referencedAssemblyNames)
    {
        return referencedAssemblyNames
            .Where(name => { return name.StartsWith(ProductPrefix, StringComparison.Ordinal); })
            .Where(name => { return !AllowedProjects.Contains(name, StringComparer.Ordinal) && name != OwnAssembly; })
            .Select(name => { return $"{CompiledReferenceNotAllowed}: Maran.Packaging is compiled against {name}."; })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads the <c>Include</c> value of every element of one kind.</summary>
    /// <param name="project">The parsed project file.</param>
    /// <param name="elementName">The element name, such as <c>PackageReference</c>.</param>
    /// <returns>The values, in file order.</returns>
    private static List<string> References(XDocument project, string elementName)
    {
        return project.Descendants(elementName)
            .Select(element => { return element.Attribute("Include")?.Value; })
            .Where(value => { return !string.IsNullOrEmpty(value); })
            .Select(value => { return value!; })
            .ToList();
    }
}
