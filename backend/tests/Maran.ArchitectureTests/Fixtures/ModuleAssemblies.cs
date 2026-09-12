using System.Reflection;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// Loads the product assemblies that shipped into the test run, so a test can derive an expectation
/// from the BUILD GRAPH instead of from a number somebody wrote down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the output directory and not the compiler's reference list.</b> The C# compiler drops a
/// <c>ProjectReference</c> whose types are never used, so reading references would make a
/// referenced-but-unused module invisible — exactly the module whose absence a coverage test exists
/// to notice. What lands next to the test binary is what the run can actually see.
/// </para>
/// <para>
/// <b>Why this is a second source and not the same one.</b> The panel composes modules through
/// <c>ModuleRegistry.All</c>, a hand-written list. The set of module assemblies on disk comes from
/// the solution's project references instead. Comparing one against the other is what lets a census
/// assert a VALUE — "every module compiled in was walked" — where it would otherwise only be able to
/// assert a floor, and a floor is satisfied by a walk that has quietly stopped seeing things.
/// </para>
/// </remarks>
public static class ModuleAssemblies
{
    /// <summary>The assembly-name prefix every module of this product carries.</summary>
    private const string ModulePrefix = "Maran.Modules.";

    /// <summary>Every product assembly sitting next to the test binary.</summary>
    /// <returns>All loaded assemblies whose name begins with <c>Maran</c>.</returns>
    public static List<Assembly> All()
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
            .Where(assembly =>
            {
                return assembly.GetName().Name?.StartsWith("Maran", StringComparison.Ordinal) == true;
            })
            .ToList();
    }

    /// <summary>Every module assembly sitting next to the test binary.</summary>
    /// <returns>The assemblies named <c>Maran.Modules.*</c>, in a stable order.</returns>
    public static List<Assembly> Modules()
    {
        return All()
            .Where(assembly =>
            {
                return assembly.GetName().Name?.StartsWith(ModulePrefix, StringComparison.Ordinal) == true;
            })
            .OrderBy(assembly =>
            {
                return assembly.GetName().Name;
            }, StringComparer.Ordinal)
            .ToList();
    }
}
