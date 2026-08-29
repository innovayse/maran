using System.Reflection;
using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// Guards the isolation suite itself. NetArchTest can only judge assemblies that are actually
/// loaded, so a module missing from this project's references would make
/// <see cref="ModuleIsolationTests"/> pass vacuously — the boundary would stop being enforced
/// exactly when a new module needs it most. These tests fail loudly in that case.
/// </summary>
public sealed class ModuleCoverageTests
{
    /// <summary>Matches the module project entries of the solution file.</summary>
    private static readonly Regex ModuleProjectPattern =
        new(@"""(Maran\.Modules\.[A-Za-z0-9.]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_module_project_in_the_solution_is_referenced_by_the_architecture_tests()
    {
        var solutionModules = ReadModuleProjectNamesFromSolution();
        var loadedAssemblies = LoadAllReferencedAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = solutionModules
            .Where(module => !module.EndsWith(".Tests", StringComparison.Ordinal))
            .Where(module => !loadedAssemblies.Contains(module))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These module projects exist in Maran.sln but are not referenced by "
            + $"Maran.ArchitectureTests, so the isolation rules never see them: {string.Join(", ", missing)}. "
            + "Add a ProjectReference for each.");
    }

    [Fact]
    public void Isolation_rules_run_against_the_projects_they_claim_to_cover()
    {
        var loaded = LoadAllReferencedAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        // The always-present projects the isolation suite asserts about. If one of these is
        // absent, its rule is silently vacuous.
        Assert.Contains("Maran.SharedKernel", loaded);
        Assert.Contains("Maran.Sdk", loaded);
    }

    /// <summary>Reads module project names straight from the solution — the source of truth.</summary>
    /// <returns>Project names such as <c>Maran.Modules.Sites</c>; empty when none exist yet.</returns>
    private static List<string> ReadModuleProjectNamesFromSolution()
    {
        var solutionPath = FindSolutionPath();
        var text = File.ReadAllText(solutionPath);
        return ModuleProjectPattern.Matches(text)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Walks up from the test binary to locate <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the solution file.</returns>
    private static string FindSolutionPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Maran.sln");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Maran.sln not found above the test output directory.");
    }

    /// <summary>
    /// Loads every Maran assembly sitting next to the test binary. Reading the compiler's
    /// reference list is not enough: the C# compiler drops a ProjectReference whose types are never
    /// used, so a referenced-but-unused module would stay invisible to NetArchTest. Scanning the
    /// output directory sees what actually shipped into the test run.
    /// </summary>
    /// <returns>All loaded assemblies belonging to this product.</returns>
    private static List<Assembly> LoadAllReferencedAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // Native or mixed-mode files matching the pattern are not managed assemblies.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Maran", StringComparison.Ordinal) == true)
            .ToList();
    }
}
