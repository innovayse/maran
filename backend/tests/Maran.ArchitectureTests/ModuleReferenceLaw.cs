using System.Xml.Linq;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rules <see cref="ModuleSdkOnlyReferenceTests"/> enforces on what a packageable module
/// references: the Sdk and the SharedKernel, the agent client only when its manifest declares agent
/// capabilities, packages and frameworks only from the allow-list, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The project file is the declaration and the compiled assembly is the evidence, and the two are
/// judged separately. The project file names what a module CAN reach (a package it lists but does not
/// use still ships into the panel process); the assembly's references name what the compiler bound,
/// which is where a project reference that the build graph pulled in transitively would show.
/// </para>
/// <para>
/// rules/architecture.md used to say a module references only the Sdk and the SharedKernel, while
/// every module that talks to the agent references <c>Maran.Agent.Client</c>. The rule here is the
/// true one: the agent client is the one further reference, and it is allowed exactly when the
/// manifest declares what it will be used for, which is the same declaration
/// <c>AgentCapabilityGuard</c> holds the module's signatures to.
/// </para>
/// <para>
/// UNOBSERVED HERE: a package's own transitive dependencies (an allow-listed package may bring more),
/// and anything loaded by name at run time. The allow-list is a statement about what the project file
/// names, not about the closure of the build.
/// </para>
/// </remarks>
public static class ModuleReferenceLaw
{
    /// <summary>The assembly every module may reference.</summary>
    public const string SdkProject = "Maran.Sdk";

    /// <summary>The assembly every module may reference.</summary>
    public const string SharedKernelProject = "Maran.SharedKernel";

    /// <summary>The assembly a module may reference only when its manifest declares agent capabilities.</summary>
    public const string AgentClientProject = "Maran.Agent.Client";

    /// <summary>A project reference other than the Sdk, the SharedKernel and a declared agent client.</summary>
    public const string ProjectReferenceNotAllowed = "ProjectReferenceNotAllowed";

    /// <summary>The agent client is referenced and the manifest declares no agent capability.</summary>
    public const string AgentClientNotDeclared = "AgentClientNotDeclared";

    /// <summary>A package the allow-list does not name.</summary>
    public const string PackageNotAllowListed = "PackageNotAllowListed";

    /// <summary>A framework reference the allow-list does not name.</summary>
    public const string FrameworkNotAllowListed = "FrameworkNotAllowListed";

    /// <summary>A bare assembly reference, which has no version and no origin.</summary>
    public const string AssemblyReferenceNotAllowed = "AssemblyReferenceNotAllowed";

    /// <summary>The compiled assembly binds to a product assembly the contract does not allow.</summary>
    public const string CompiledReferenceNotAllowed = "CompiledReferenceNotAllowed";

    /// <summary>An allow-list entry that no packageable module uses.</summary>
    public const string AllowListEntryUnused = "AllowListEntryUnused";

    /// <summary>Names every way one module's project file breaks the reference contract.</summary>
    /// <param name="moduleId">The module's id, for the messages.</param>
    /// <param name="projectText">The text of its <c>.csproj</c>.</param>
    /// <param name="declaresAgentCapabilities">Whether its manifest declares at least one agent capability.</param>
    /// <param name="allowList">The packages and frameworks a module may reference.</param>
    /// <returns>One line per violation, each beginning with the rule's constant.</returns>
    public static IReadOnlyList<string> ProjectViolations(
        string moduleId,
        string projectText,
        bool declaresAgentCapabilities,
        ModuleReferenceAllowList allowList)
    {
        var project = XDocument.Parse(projectText);
        var found = new List<string>();

        foreach (var name in References(project, "ProjectReference").Select(ProjectNameOf))
        {
            if (string.Equals(name, AgentClientProject, StringComparison.Ordinal))
            {
                if (!declaresAgentCapabilities)
                {
                    found.Add($"{AgentClientNotDeclared}: '{moduleId}' references {name} and declares no agent capability.");
                }
            }
            else if (!IsSdkOrKernel(name))
            {
                found.Add($"{ProjectReferenceNotAllowed}: '{moduleId}' references project {name}.");
            }
        }

        foreach (var package in References(project, "PackageReference").Where(name => { return !allowList.Packages.Contains(name); }))
        {
            found.Add($"{PackageNotAllowListed}: '{moduleId}' references package {package}.");
        }

        foreach (var framework in References(project, "FrameworkReference").Where(name => { return !allowList.Frameworks.Contains(name); }))
        {
            found.Add($"{FrameworkNotAllowListed}: '{moduleId}' references framework {framework}.");
        }

        foreach (var assembly in References(project, "Reference"))
        {
            found.Add($"{AssemblyReferenceNotAllowed}: '{moduleId}' references assembly {assembly} directly.");
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Names every product assembly a compiled module binds to that the contract does not allow.</summary>
    /// <param name="moduleId">The module's id, for the messages.</param>
    /// <param name="referencedAssemblyNames">The names the compiled assembly references.</param>
    /// <param name="declaresAgentCapabilities">Whether its manifest declares at least one agent capability.</param>
    /// <returns>One line per violation, each beginning with the rule's constant.</returns>
    public static IReadOnlyList<string> CompiledViolations(
        string moduleId,
        IEnumerable<string> referencedAssemblyNames,
        bool declaresAgentCapabilities)
    {
        return referencedAssemblyNames
            .Where(name => { return name.StartsWith("Maran.", StringComparison.Ordinal); })
            .Where(name =>
            {
                return !IsSdkOrKernel(name)
                    && !(declaresAgentCapabilities && string.Equals(name, AgentClientProject, StringComparison.Ordinal));
            })
            .Select(name => { return $"{CompiledReferenceNotAllowed}: '{moduleId}' is compiled against {name}."; })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Names the allow-list entries that none of the examined project files uses.</summary>
    /// <param name="projectTexts">The text of every examined module's <c>.csproj</c>.</param>
    /// <param name="allowList">The list to hold against them.</param>
    /// <returns>One line per unused entry.</returns>
    public static IReadOnlyList<string> UnusedEntries(IEnumerable<string> projectTexts, ModuleReferenceAllowList allowList)
    {
        var projects = projectTexts.Select(text => { return XDocument.Parse(text); }).ToList();
        var usedPackages = projects.SelectMany(project => { return References(project, "PackageReference"); }).ToHashSet(StringComparer.Ordinal);
        var usedFrameworks = projects.SelectMany(project => { return References(project, "FrameworkReference"); }).ToHashSet(StringComparer.Ordinal);

        return allowList.Packages.Where(name => { return !usedPackages.Contains(name); })
            .Concat(allowList.Frameworks.Where(name => { return !usedFrameworks.Contains(name); }))
            .Order(StringComparer.Ordinal)
            .Select(name => { return $"{AllowListEntryUnused}: '{name}' is allow-listed and no packageable module references it."; })
            .ToList();
    }

    /// <summary>Whether a project name is the Sdk or the SharedKernel.</summary>
    /// <param name="name">The project or assembly name.</param>
    /// <returns><c>true</c> for the two projects every module may reference.</returns>
    private static bool IsSdkOrKernel(string name)
    {
        return string.Equals(name, SdkProject, StringComparison.Ordinal)
            || string.Equals(name, SharedKernelProject, StringComparison.Ordinal);
    }

    /// <summary>Reads the <c>Include</c> or <c>Update</c> value of every element of one kind.</summary>
    /// <param name="project">The parsed project file.</param>
    /// <param name="elementName">The element name, such as <c>PackageReference</c>.</param>
    /// <returns>The values, in file order.</returns>
    private static List<string> References(XDocument project, string elementName)
    {
        return project.Descendants(elementName)
            .Select(element => { return element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value; })
            .Where(value => { return !string.IsNullOrEmpty(value); })
            .Select(value => { return value!; })
            .ToList();
    }

    /// <summary>Reduces a project reference path to the project's name.</summary>
    /// <param name="path">The <c>Include</c> value, with either path separator.</param>
    /// <returns>The file name without its extension.</returns>
    private static string ProjectNameOf(string path)
    {
        return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    }
}
