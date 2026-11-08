using System.Text.RegularExpressions;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rules <see cref="ModuleContractTests"/> enforces on what a module publishes about itself:
/// identity, version, schema, permissions, menu and the Core/Packageable split.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a law over primitives and not a call into the Sdk's manifest validator.</b> The validator
/// is what the loader runs on a package it has not seen; this law is the repository's own gate over
/// the modules it ships. Two readers of the same contract, written separately, cannot share one bug,
/// and this one needs rules the validator has no business knowing (the module's
/// <c>IPanelModule.Name</c>, the controllers' policies, the composed set).
/// </para>
/// <para>
/// <b>Where it is strict.</b> The rules that exist so a module can leave the repository — every
/// controller policy declared, every menu entry pointing at a route of its own — bind
/// <see cref="ModuleKind.Packageable"/> modules only: a core module never ships as a package, and
/// binding it to rules about shipping would be rules about nothing. Identity, version, schema,
/// permission-vocabulary, uniqueness and dependency rules bind every module, because the Host reads
/// those for all of them.
/// </para>
/// <para>
/// UNOBSERVED HERE: that a declared permission is actually enforced on an endpoint beyond the
/// controller attributes (a policy applied by route-group metadata is not an attribute), and that a
/// <c>MinPanelVersion</c> is truthful — a number a human wrote cannot be proven true by a test.
/// </para>
/// </remarks>
public static class ModuleContractLaw
{
    /// <summary>What a module id must look like to double as a PostgreSQL schema name.</summary>
    private static readonly Regex SchemaShape =
        new("^[a-z][a-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Names every way a set of modules breaks the contract.</summary>
    /// <param name="subjects">The modules to judge.</param>
    /// <param name="panel">The panel version the modules must be hostable by.</param>
    /// <param name="vocabulary">The policy names the Sdk defines.</param>
    /// <returns>The violations, ordered; empty when the set obeys the contract.</returns>
    public static IReadOnlyList<ModuleContractViolation> Violations(
        IReadOnlyList<ModuleContractSubject> subjects,
        SemanticVersion panel,
        IReadOnlySet<string> vocabulary)
    {
        var found = new List<ModuleContractViolation>();
        foreach (var subject in subjects)
        {
            found.AddRange(IdentityViolations(subject));
            found.AddRange(VersionViolations(subject, panel));
            found.AddRange(PermissionViolations(subject, vocabulary));
            found.AddRange(MenuViolations(subject));
            found.AddRange(KindViolations(subject));
        }

        found.AddRange(Duplicates(subjects));
        found.AddRange(ModuleDependencyLaw.Violations(subjects));

        return found
            .OrderBy(violation => { return violation.ModuleId; }, StringComparer.Ordinal)
            .ThenBy(violation => { return violation.Rule; })
            .ThenBy(violation => { return violation.Detail; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Judges the id against the module's name and against the shape a schema name needs.</summary>
    /// <param name="subject">The module to judge.</param>
    /// <returns>The identity violations of this module.</returns>
    private static List<ModuleContractViolation> IdentityViolations(ModuleContractSubject subject)
    {
        var manifest = subject.Manifest;
        var found = new List<ModuleContractViolation>();

        if (!string.Equals(manifest.Id, subject.ModuleName, StringComparison.Ordinal))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.IdIsNotName,
                manifest.Id,
                $"publishes id '{manifest.Id}' but IPanelModule.Name is '{subject.ModuleName}'."));
        }

        if (!SchemaShape.IsMatch(manifest.Id))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.IdIsNotSchemaShaped,
                manifest.Id,
                "has an id that is not lower-case letters and digits, so it cannot be its own schema name."));
        }

        if (manifest.SchemaName is not null && !string.Equals(manifest.SchemaName, manifest.Id, StringComparison.Ordinal))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.SchemaNameIsNotId,
                manifest.Id,
                $"declares schema '{manifest.SchemaName}'; a module owns the schema named after it, or none."));
        }

        return found;
    }

    /// <summary>Judges the module's version and the lowest panel version that can host it.</summary>
    /// <param name="subject">The module to judge.</param>
    /// <param name="panel">The panel version this build is.</param>
    /// <returns>The version violations of this module.</returns>
    private static List<ModuleContractViolation> VersionViolations(ModuleContractSubject subject, SemanticVersion panel)
    {
        var manifest = subject.Manifest;
        var found = new List<ModuleContractViolation>();

        if (!SemanticVersion.TryParse(manifest.Version, out _))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.VersionIsNotSemantic,
                manifest.Id,
                $"has version '{manifest.Version}', which is not a semantic version."));
        }

        if (!SemanticVersion.TryParse(manifest.MinPanelVersion, out var minimum))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.MinPanelVersionIsNotSemantic,
                manifest.Id,
                $"has MinPanelVersion '{manifest.MinPanelVersion}', which is blank or not a semantic version."));
        }
        else if (minimum!.CompareTo(panel) > 0)
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.MinPanelVersionAbovePanel,
                manifest.Id,
                $"needs panel {manifest.MinPanelVersion}, above the panel this build is."));
        }

        return found;
    }

    /// <summary>Judges the declared permissions against the Sdk vocabulary, and the controllers against them.</summary>
    /// <param name="subject">The module to judge.</param>
    /// <param name="vocabulary">The policy names the Sdk defines.</param>
    /// <returns>The permission violations of this module.</returns>
    private static List<ModuleContractViolation> PermissionViolations(
        ModuleContractSubject subject,
        IReadOnlySet<string> vocabulary)
    {
        var manifest = subject.Manifest;
        var found = new List<ModuleContractViolation>();

        foreach (var permission in manifest.Permissions.Where(entry => { return !vocabulary.Contains(entry); }))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.PermissionOutsideVocabulary,
                manifest.Id,
                $"declares permission '{permission}', which is not a policy name the Sdk defines."));
        }

        if (manifest.Kind == ModuleKind.Packageable)
        {
            var declared = manifest.Permissions.ToHashSet(StringComparer.Ordinal);
            foreach (var policy in subject.ControllerPolicies.Where(entry => { return !declared.Contains(entry); }))
            {
                found.Add(new ModuleContractViolation(
                    ModuleContractRule.ControllerPolicyNotDeclared,
                    manifest.Id,
                    $"has a controller naming policy '{policy}' that its manifest does not declare."));
            }
        }

        return found;
    }

    /// <summary>Judges a packageable module's menu entries against its own routes.</summary>
    /// <param name="subject">The module to judge.</param>
    /// <returns>The menu violations of this module.</returns>
    private static List<ModuleContractViolation> MenuViolations(ModuleContractSubject subject)
    {
        var manifest = subject.Manifest;
        var found = new List<ModuleContractViolation>();
        if (manifest.Kind != ModuleKind.Packageable)
        {
            return found;
        }

        var routes = manifest.Routes.Select(route => { return route.Name; }).ToHashSet(StringComparer.Ordinal);
        foreach (var item in manifest.Menu.Where(entry => { return !routes.Contains(entry.RouteName); }))
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.MenuPointsAtNoRoute,
                manifest.Id,
                $"has menu entry '{item.Key}' pointing at route '{item.RouteName}', which it does not declare."));
        }

        return found;
    }

    /// <summary>Judges the core module's standing: never gated by a licence.</summary>
    /// <param name="subject">The module to judge.</param>
    /// <returns>The kind violations of this module.</returns>
    private static List<ModuleContractViolation> KindViolations(ModuleContractSubject subject)
    {
        var manifest = subject.Manifest;
        var found = new List<ModuleContractViolation>();

        if (manifest.Kind == ModuleKind.Core && manifest.Tier != LicenceTier.Included)
        {
            found.Add(new ModuleContractViolation(
                ModuleContractRule.CoreIsGated,
                manifest.Id,
                $"is core but sits in licence tier {manifest.Tier}; losing a licence must never remove a core module."));
        }

        return found;
    }

    /// <summary>Names every id, menu key and route name two modules (or two entries) share.</summary>
    /// <param name="subjects">The modules to judge.</param>
    /// <returns>One violation per owner of a repeated value.</returns>
    private static List<ModuleContractViolation> Duplicates(IReadOnlyList<ModuleContractSubject> subjects)
    {
        var found = new List<ModuleContractViolation>();
        found.AddRange(Repeats(
            subjects, ModuleContractRule.DuplicateModuleId, "id", subject => { return [subject.Manifest.Id]; }));
        found.AddRange(Repeats(
            subjects,
            ModuleContractRule.DuplicateMenuKey,
            "menu key",
            subject => { return subject.Manifest.Menu.Select(item => { return item.Key; }); }));
        found.AddRange(Repeats(
            subjects,
            ModuleContractRule.DuplicateRouteName,
            "route name",
            subject => { return subject.Manifest.Routes.Select(route => { return route.Name; }); }));
        return found;
    }

    /// <summary>Finds the values one selector yields more than once across the whole set.</summary>
    /// <param name="subjects">The modules to judge.</param>
    /// <param name="rule">The rule a repeat breaks.</param>
    /// <param name="noun">What the value is, for the message.</param>
    /// <param name="select">Reads the values of one module.</param>
    /// <returns>One violation per module carrying a repeated value.</returns>
    private static List<ModuleContractViolation> Repeats(
        IReadOnlyList<ModuleContractSubject> subjects,
        ModuleContractRule rule,
        string noun,
        Func<ModuleContractSubject, IEnumerable<string>> select)
    {
        var owned = subjects
            .SelectMany(subject => { return select(subject).Select(value => { return (value, subject.Manifest.Id); }); })
            .ToList();

        return owned
            .GroupBy(pair => { return pair.value; }, StringComparer.Ordinal)
            .Where(group => { return group.Count() > 1; })
            .SelectMany(group =>
            {
                return group.Select(pair =>
                {
                    return new ModuleContractViolation(rule, pair.Id, $"shares {noun} '{group.Key}' with another entry.");
                });
            })
            .ToList();
    }
}
