using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rules about <c>Manifest.Dependencies</c>: every one names a module that exists, none names its
/// own module, the graph has no cycle, and the core never depends on something that can be removed.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the Host read <c>Dependencies</c> before this law (the list was documentation), so a
/// typo in an id was a dependency on nothing. The loader composes modules in dependency order, and
/// a cycle there is a module that can never be first.
/// </para>
/// <para>
/// UNOBSERVED HERE: the version range of a dependency. Whether the dependency's published version
/// satisfies the range is the loader's question at install time; this law reads ids only.
/// </para>
/// </remarks>
public static class ModuleDependencyLaw
{
    /// <summary>Names every way the dependency graph of a set of modules breaks the contract.</summary>
    /// <param name="subjects">The modules to judge.</param>
    /// <returns>The violations, unordered.</returns>
    public static IReadOnlyList<ModuleContractViolation> Violations(IReadOnlyList<ModuleContractSubject> subjects)
    {
        var byId = new Dictionary<string, ModuleContractSubject>(StringComparer.Ordinal);
        foreach (var subject in subjects)
        {
            byId.TryAdd(subject.Manifest.Id, subject);
        }

        var found = new List<ModuleContractViolation>();
        foreach (var subject in subjects)
        {
            var manifest = subject.Manifest;
            foreach (var dependency in manifest.Dependencies)
            {
                found.AddRange(DependencyViolations(manifest, dependency.Id, byId));
            }

            var cycle = CycleThrough(manifest.Id, byId);
            if (cycle is not null)
            {
                found.Add(new ModuleContractViolation(
                    ModuleContractRule.DependencyCycle,
                    manifest.Id,
                    $"is on a dependency cycle: {cycle}."));
            }
        }

        return found;
    }

    /// <summary>Judges one declared dependency.</summary>
    /// <param name="manifest">The module declaring it.</param>
    /// <param name="dependencyId">The id it depends on.</param>
    /// <param name="byId">Every composed module by id.</param>
    /// <returns>Zero or more violations.</returns>
    private static List<ModuleContractViolation> DependencyViolations(
        Manifest manifest,
        string dependencyId,
        Dictionary<string, ModuleContractSubject> byId)
    {
        var found = new List<ModuleContractViolation>();
        if (string.Equals(dependencyId, manifest.Id, StringComparison.Ordinal))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.DependencyOnSelf,
                manifest.Id,
                "lists itself as a dependency."));
        }
        else if (!byId.TryGetValue(dependencyId, out var target))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.DependencyUnknown,
                manifest.Id,
                $"depends on '{dependencyId}', which no composed module publishes."));
        }
        else if (manifest.Kind == ModuleKind.Core && target.Manifest.Kind == ModuleKind.Packageable)
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.CoreDependsOnPackageable,
                manifest.Id,
                $"is core but depends on packageable module '{dependencyId}'."));
        }

        return found;
    }

    /// <summary>Finds a path of dependencies leading from a module back to itself.</summary>
    /// <param name="start">The module to start from.</param>
    /// <param name="byId">Every composed module by id.</param>
    /// <returns>The path as <c>a -> b -> a</c>, or <c>null</c> when the module is on no cycle.</returns>
    private static string? CycleThrough(string start, Dictionary<string, ModuleContractSubject> byId)
    {
        var path = new List<string> { start };
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Walk(start, start, byId, path, visited) ? string.Join(" -> ", path) : null;
    }

    /// <summary>Depth-first search for <paramref name="start"/> among the dependencies of <paramref name="current"/>.</summary>
    /// <param name="start">The module a cycle must return to.</param>
    /// <param name="current">The module whose dependencies are followed.</param>
    /// <param name="byId">Every composed module by id.</param>
    /// <param name="path">The route taken so far; extended with the closing step when found.</param>
    /// <param name="visited">Modules already expanded, so a shared tail is walked once.</param>
    /// <returns><c>true</c> when a dependency leads back to <paramref name="start"/>.</returns>
    private static bool Walk(
        string start,
        string current,
        IReadOnlyDictionary<string, ModuleContractSubject> byId,
        List<string> path,
        HashSet<string> visited)
    {
        if (!byId.TryGetValue(current, out var subject))
        {
            return false;
        }

        foreach (var dependency in subject.Manifest.Dependencies)
        {
            if (string.Equals(dependency.Id, start, StringComparison.Ordinal))
            {
                path.Add(start);
                return true;
            }

            if (!visited.Add(dependency.Id))
            {
                continue;
            }

            path.Add(dependency.Id);
            if (Walk(start, dependency.Id, byId, path, visited))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }
}
