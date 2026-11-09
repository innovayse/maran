using Maran.Sdk.Contracts;
using Maran.Sdk.Manifests.Enums;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Sdk.Manifests;

/// <summary>
/// The rules a <see cref="Manifest"/> must satisfy before the panel composes the module it describes,
/// as pure functions from manifests to findings.
/// </summary>
/// <remarks>
/// <para>
/// Two entry points, for two questions. <see cref="Validate"/> asks what one manifest can be judged on
/// by itself, given the ids already known (the loader asks it of a package it is about to register).
/// <see cref="ValidateSet"/> asks the same of a whole composed set, and adds the rules that only a set
/// can break: duplicate ids, menu keys and route names shared between modules, a dependency whose
/// version is outside its range, a cycle, and a core module resting on a packageable one.
/// </para>
/// <para>
/// A finding is data, not an exception, and an empty list is the only success. Nothing here reads a
/// file, loads an assembly or holds a key. The rules describe what a module DECLARES: a hostile module
/// can declare falsely, so a clean result is evidence about a module the repository builds and advice
/// to the administrator about a downloaded one, never a sandbox (threat note
/// <c>2026-11-07-marketplace-loader-threat-note.md</c>).
/// </para>
/// </remarks>
public static class ManifestValidator
{
    /// <summary>The longest module id: a schema name, which PostgreSQL limits to 63 bytes, with room to spare.</summary>
    private const int MaxIdLength = 32;

    /// <summary>Judges one manifest on what it declares about itself.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="panelVersion">The version of the running panel.</param>
    /// <param name="knownModuleIds">The ids of the modules composed beside this one, this module's own included.</param>
    /// <param name="knownPolicies">The policy names a permission may take (<see cref="ManifestPermissionVocabulary.Policies"/>).</param>
    /// <returns>Every rule the manifest broke; empty when it broke none.</returns>
    public static IReadOnlyList<ManifestFinding> Validate(
        Manifest manifest,
        SemanticVersion panelVersion,
        IReadOnlySet<string> knownModuleIds,
        IReadOnlySet<string> knownPolicies)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(panelVersion);
        ArgumentNullException.ThrowIfNull(knownModuleIds);
        ArgumentNullException.ThrowIfNull(knownPolicies);

        var findings = new List<ManifestFinding>();

        CheckIdentity(manifest, panelVersion, findings);
        CheckPermissions(manifest, knownPolicies, findings);
        CheckNavigation(manifest, knownModuleIds, findings);
        CheckDependencies(manifest, knownModuleIds, findings);

        return findings;
    }

    /// <summary>Judges a whole composed set: every manifest on its own, then the rules only a set can break.</summary>
    /// <param name="manifests">Every manifest composed into the panel.</param>
    /// <param name="panelVersion">The version of the running panel.</param>
    /// <param name="knownPolicies">The policy names a permission may take (<see cref="ManifestPermissionVocabulary.Policies"/>).</param>
    /// <returns>Every rule the set broke; empty when it broke none.</returns>
    public static IReadOnlyList<ManifestFinding> ValidateSet(
        IReadOnlyList<Manifest> manifests,
        SemanticVersion panelVersion,
        IReadOnlySet<string> knownPolicies)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var knownIds = manifests.Select(manifest =>
        {
            return manifest.Id;
        }).ToHashSet(StringComparer.Ordinal);
        var findings = new List<ManifestFinding>();

        foreach (var manifest in manifests)
        {
            findings.AddRange(Validate(manifest, panelVersion, knownIds, knownPolicies));
        }

        CheckUniqueness(manifests, findings);
        CheckDependencyGraph(manifests, findings);

        return findings;
    }

    /// <summary>True for lowercase ASCII letters and digits, beginning with a letter, within the length limit.</summary>
    /// <param name="id">The id to test.</param>
    /// <returns>True when the id is well formed.</returns>
    public static bool IsWellFormedId(string id)
    {
        return id.Length is > 0 and <= MaxIdLength
            && char.IsAsciiLetterLower(id[0])
            && id.All(c =>
            {
                return char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c);
            });
    }

    /// <summary>Checks the module's id, versions and schema.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="panelVersion">The version of the running panel.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckIdentity(Manifest manifest, SemanticVersion panelVersion, List<ManifestFinding> findings)
    {
        if (!IsWellFormedId(manifest.Id))
        {
            findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.IdMalformed, manifest.Id));
        }

        if (!SemanticVersion.TryParse(manifest.Version, out _))
        {
            findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.VersionInvalid, manifest.Version));
        }

        if (!SemanticVersion.TryParse(manifest.MinPanelVersion, out var minimum))
        {
            findings.Add(new ManifestFinding(
                manifest.Id,
                ManifestFindingKind.MinPanelVersionInvalid,
                manifest.MinPanelVersion));
        }
        else if (SemanticVersion.Compare(minimum!, panelVersion) > 0)
        {
            findings.Add(new ManifestFinding(
                manifest.Id,
                ManifestFindingKind.MinPanelVersionAbovePanel,
                manifest.MinPanelVersion));
        }

        if (manifest.SchemaName is not null && !string.Equals(manifest.SchemaName, manifest.Id, StringComparison.Ordinal))
        {
            findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.SchemaNameNotId, manifest.SchemaName));
        }
    }

    /// <summary>Checks every permission against the policy vocabulary.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="knownPolicies">The policy names a permission may take.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckPermissions(Manifest manifest, IReadOnlySet<string> knownPolicies, List<ManifestFinding> findings)
    {
        foreach (var permission in manifest.Permissions)
        {
            if (!knownPolicies.Contains(permission))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.PermissionUnknown, permission));
            }
        }
    }

    /// <summary>Checks the menu and routes for duplicates inside the module, and routes for the module they require.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="knownModuleIds">The ids of the modules composed beside this one.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckNavigation(Manifest manifest, IReadOnlySet<string> knownModuleIds, List<ManifestFinding> findings)
    {
        foreach (var key in Duplicates(manifest.Menu.Select(item =>
        {
            return item.Key;
        })))
        {
            findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DuplicateMenuKey, key));
        }

        foreach (var name in Duplicates(manifest.Routes.Select(route =>
        {
            return route.Name;
        })))
        {
            findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DuplicateRouteName, name));
        }

        foreach (var route in manifest.Routes)
        {
            var required = route.RequiresModule;

            if (required is not null
                && (string.Equals(required, manifest.Id, StringComparison.Ordinal) || !knownModuleIds.Contains(required)))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.RouteRequiresUnknownModule, required));
            }
        }
    }

    /// <summary>Checks each dependency's range and that the module it names is composed; a module depending on itself is a cycle.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="knownModuleIds">The ids of the modules composed beside this one.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckDependencies(Manifest manifest, IReadOnlySet<string> knownModuleIds, List<ManifestFinding> findings)
    {
        foreach (var dependency in manifest.Dependencies)
        {
            if (!VersionRange.TryParse(dependency.Range, out _))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DependencyRangeInvalid, dependency.Id));
            }

            if (string.Equals(dependency.Id, manifest.Id, StringComparison.Ordinal))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DependencyCycle, dependency.Id));
            }
            else if (!knownModuleIds.Contains(dependency.Id))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DependencyUnknown, dependency.Id));
            }
        }
    }

    /// <summary>Checks ids, menu keys and route names that two different modules both claim.</summary>
    /// <param name="manifests">Every manifest composed into the panel.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckUniqueness(IReadOnlyList<Manifest> manifests, List<ManifestFinding> findings)
    {
        foreach (var id in Duplicates(manifests.Select(manifest =>
        {
            return manifest.Id;
        })))
        {
            findings.Add(new ManifestFinding(id, ManifestFindingKind.DuplicateModuleId, id));
        }

        var keyOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var nameOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var manifest in manifests)
        {
            ClaimAcrossModules(
                manifest.Id,
                manifest.Menu.Select(item =>
                {
                    return item.Key;
                }).Distinct(StringComparer.Ordinal),
                keyOwners,
                ManifestFindingKind.DuplicateMenuKey,
                findings);
            ClaimAcrossModules(
                manifest.Id,
                manifest.Routes.Select(route =>
                {
                    return route.Name;
                }).Distinct(StringComparer.Ordinal),
                nameOwners,
                ManifestFindingKind.DuplicateRouteName,
                findings);
        }
    }

    /// <summary>Records which module first claimed each value, and reports a value a later module claims too.</summary>
    /// <param name="moduleId">The module claiming the values.</param>
    /// <param name="values">The module's distinct values.</param>
    /// <param name="owners">The first claimant of each value seen so far; updated.</param>
    /// <param name="kind">The finding to raise for a value already claimed by another module.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void ClaimAcrossModules(
        string moduleId,
        IEnumerable<string> values,
        Dictionary<string, string> owners,
        ManifestFindingKind kind,
        List<ManifestFinding> findings)
    {
        foreach (var value in values)
        {
            if (owners.TryGetValue(value, out var owner) && !string.Equals(owner, moduleId, StringComparison.Ordinal))
            {
                findings.Add(new ManifestFinding(moduleId, kind, value));
            }
            else
            {
                owners.TryAdd(value, moduleId);
            }
        }
    }

    /// <summary>Checks version ranges against the modules they name, core against packageable, and cycles.</summary>
    /// <param name="manifests">Every manifest composed into the panel.</param>
    /// <param name="findings">Where findings are added.</param>
    private static void CheckDependencyGraph(IReadOnlyList<Manifest> manifests, List<ManifestFinding> findings)
    {
        var byId = new Dictionary<string, Manifest>(StringComparer.Ordinal);

        foreach (var manifest in manifests)
        {
            byId.TryAdd(manifest.Id, manifest);
        }

        foreach (var manifest in manifests)
        {
            foreach (var dependency in manifest.Dependencies)
            {
                if (!byId.TryGetValue(dependency.Id, out var target)
                    || string.Equals(dependency.Id, manifest.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (VersionRange.TryParse(dependency.Range, out var range)
                    && SemanticVersion.TryParse(target.Version, out var version)
                    && !range!.Contains(version!))
                {
                    findings.Add(new ManifestFinding(
                        manifest.Id,
                        ManifestFindingKind.DependencyVersionUnsatisfied,
                        dependency.Id));
                }

                if (manifest.Kind == ModuleKind.Core && target.Kind == ModuleKind.Packageable)
                {
                    findings.Add(new ManifestFinding(
                        manifest.Id,
                        ManifestFindingKind.CoreDependsOnPackageable,
                        dependency.Id));
                }
            }

            if (ReachesItself(manifest.Id, byId))
            {
                findings.Add(new ManifestFinding(manifest.Id, ManifestFindingKind.DependencyCycle, manifest.Id));
            }
        }
    }

    /// <summary>True when following dependencies (other than a module on itself) from a module returns to it.</summary>
    /// <param name="start">The module to start from.</param>
    /// <param name="byId">Every composed manifest by id.</param>
    /// <returns>True when the module is on a dependency cycle.</returns>
    private static bool ReachesItself(string start, Dictionary<string, Manifest> byId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!byId.TryGetValue(current, out var manifest))
            {
                continue;
            }

            foreach (var dependency in manifest.Dependencies)
            {
                if (string.Equals(dependency.Id, current, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(dependency.Id, start, StringComparison.Ordinal))
                {
                    return true;
                }

                if (visited.Add(dependency.Id))
                {
                    pending.Push(dependency.Id);
                }
            }
        }

        return false;
    }

    /// <summary>The values that appear more than once, each reported once, in first-seen order.</summary>
    /// <param name="values">The values to examine.</param>
    /// <returns>The duplicated values.</returns>
    private static IEnumerable<string> Duplicates(IEnumerable<string> values)
    {
        return values
            .GroupBy(value =>
            {
                return value;
            }, StringComparer.Ordinal)
            .Where(group =>
            {
                return group.Count() > 1;
            })
            .Select(group =>
            {
                return group.Key;
            });
    }
}
