using System.Reflection;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Manifests;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Host.Modules;

/// <summary>
/// Decides which of the modules that arrived as packages join the modules that ship with the panel.
/// Each candidate is judged on its own against the set composed so far, and the first rule it breaks
/// is its refusal; a candidate that passes joins, so a package that depends on an earlier package sees
/// it. A refused module is reported and left out - the panel starts without it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a refusal is data here when the guards beside it throw.</b> <see cref="AgentCapabilityGuard"/>
/// and <see cref="ModuleManifestGuard"/> judge the modules this repository ships, where a violation is a
/// defect the build must fail on. A package is something an administrator installed from somewhere else:
/// one that is wrong must not stop the panel from starting, or a single bad file takes the whole
/// server's control panel down with it (rules/security.md; the threat note's "a corrupt installed
/// package does not stop the panel"). The same rules apply - this type calls them - but their verdict
/// removes one module instead of ending the start.
/// </para>
/// <para>
/// <b>What it checks, in order.</b> The manifest can be read at all and has every member (a getter that
/// throws, answers null or leaves a member null is refused as unreadable, and the panel starts); the
/// module is declared packageable (a package cannot claim to be part of the panel); its id is not already taken, which is also what stops a package shadowing a
/// shipped module; its assembly is not one the panel reserves for itself or one a shipped module is
/// already loaded from; the agent clients its types depend on are covered by the capabilities its
/// manifest declares (<see cref="AgentCapabilityGuard"/>); and adding its manifest to the set breaks no
/// rule of the module contract that the set did not already break (<see cref="ManifestValidator"/>).
/// </para>
/// <para>
/// <b>What it does not check.</b> That the package is the one its author signed. Signature, hash and
/// licence verification are the loader's, before a module is ever offered here; this type is the
/// second line, the one that holds even for a loader that verified everything it should. It also cannot
/// stop a module that locates a service at run time or reflects its way to a client
/// (rules/security.md item 13, "Stated limit").
/// </para>
/// </remarks>
public static class PackagedModuleComposer
{
    /// <summary>
    /// Assemblies a package may never bring along or be loaded as: the contract every module is compiled
    /// against, the kernel under it, the door to the root agent and the composition root. A second copy
    /// of any of them would give the package its own type identity for the very types the checks inspect.
    /// </summary>
    private static readonly HashSet<string> ReservedAssemblyNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Maran.Sdk",
        "Maran.SharedKernel",
        "Maran.Agent.Client",
        "Maran.Host",
    };

    /// <summary>Composes the shipped modules with the packaged ones that pass every check.</summary>
    /// <param name="shipped">The modules that ship with the panel, already verified by the composition root's guards.</param>
    /// <param name="packaged">The modules the loader offers, in the order they should be registered.</param>
    /// <param name="panelVersion">The version of the panel they are composed into.</param>
    /// <returns>The composed set and the refusals; the composed set is never smaller than <paramref name="shipped"/>.</returns>
    /// <exception cref="InvalidOperationException">The panel's own version is not a semantic version.</exception>
    public static PackagedModuleComposition Compose(
        IReadOnlyList<IPanelModule> shipped,
        IReadOnlyList<IPanelModule> packaged,
        IPanelVersion panelVersion)
    {
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(packaged);
        ArgumentNullException.ThrowIfNull(panelVersion);

        if (!SemanticVersion.TryParse(panelVersion.Current, out var panel))
        {
            throw new InvalidOperationException(
                $"The panel's own version '{panelVersion.Current}' is not a semantic version, so no packaged "
                + "module's minimum panel version can be compared with it. Refusing to compose any module.");
        }

        var composed = shipped.ToList();
        var admitted = new List<IPanelModule>();
        var refused = new List<PackagedModuleRefusal>();

        foreach (var candidate in packaged)
        {
            var manifest = ReadManifest(candidate);
            if (manifest is null)
            {
                refused.Add(new PackagedModuleRefusal(null, ModuleRefusals.ManifestUnreadable));
                continue;
            }

            var reason = Judge(candidate, manifest, composed, panel!);
            if (reason is null)
            {
                composed.Add(candidate);
                admitted.Add(candidate);
            }
            else
            {
                refused.Add(new PackagedModuleRefusal(manifest, reason));
            }
        }

        return new PackagedModuleComposition(composed, admitted, refused);
    }

    /// <summary>Whether an assembly name is one the panel reserves for itself.</summary>
    /// <param name="assemblyName">The simple name of the assembly, as <see cref="AssemblyName.Name"/> spells it.</param>
    /// <returns>True for the contract, kernel, agent client and host assemblies; false for anything else, including null.</returns>
    public static bool IsReservedAssemblyName(string? assemblyName)
    {
        return assemblyName is not null && ReservedAssemblyNames.Contains(assemblyName);
    }

    /// <summary>
    /// Reads a candidate's manifest once, and only if it is whole. A package's manifest getter is code an
    /// outsider wrote: it may throw, answer null, or answer a manifest with a member missing, and any of
    /// those reaching the checks below would end the start instead of removing one module.
    /// </summary>
    /// <param name="candidate">The packaged module.</param>
    /// <returns>The manifest, or null when it cannot be read or is not whole.</returns>
    private static Manifest? ReadManifest(IPanelModule candidate)
    {
        try
        {
            var manifest = candidate.Manifest;

            return manifest is not null && IsWhole(manifest) ? manifest : null;
        }
        catch (Exception)
        {
            // Deliberately broad, with the reason on the line: the getter is the package's own code, so
            // any exception it throws means the same to the panel - this module cannot be judged - and
            // none of them may stop the start. The exception's text is not shown (rules/security.md item 8).
            return null;
        }
    }

    /// <summary>Whether every member of a manifest the checks read is present, list elements included.</summary>
    /// <param name="manifest">The manifest a candidate published.</param>
    /// <returns>False when a text, a list or an element of a list is null.</returns>
    private static bool IsWhole(Manifest manifest)
    {
        return manifest.Id is not null
            && manifest.DisplayNameKey is not null
            && manifest.Version is not null
            && manifest.MinPanelVersion is not null
            && manifest.Dependencies is not null && !manifest.Dependencies.Any(item => { return item is null; })
            && manifest.AgentCapabilities is not null
            && manifest.Permissions is not null && !manifest.Permissions.Any(item => { return item is null; })
            && manifest.Menu is not null && !manifest.Menu.Any(item => { return item is null; })
            && manifest.Routes is not null && !manifest.Routes.Any(item => { return item is null; });
    }

    /// <summary>Finds the first rule a candidate breaks.</summary>
    /// <param name="candidate">The packaged module.</param>
    /// <param name="manifest">The candidate's manifest, already read and whole.</param>
    /// <param name="composed">The modules composed so far.</param>
    /// <param name="panel">The panel's version.</param>
    /// <returns>The refusal, or null when the candidate may join.</returns>
    private static Error? Judge(IPanelModule candidate, Manifest manifest, List<IPanelModule> composed, SemanticVersion panel)
    {
        if (manifest.Kind != ModuleKind.Packageable)
        {
            return ModuleRefusals.NotPackageable;
        }

        if (composed.Any(module => { return string.Equals(module.Manifest.Id, manifest.Id, StringComparison.Ordinal); }))
        {
            return ModuleRefusals.IdTaken;
        }

        if (UsesReservedAssembly(candidate, composed))
        {
            return ModuleRefusals.AssemblyReserved;
        }

        var capabilityRefusal = JudgeCapabilities(candidate);
        if (capabilityRefusal is not null)
        {
            return capabilityRefusal;
        }

        return BreaksContract(manifest, composed, panel)
            ? ModuleRefusals.ManifestInvalid
            : null;
    }

    /// <summary>Whether the candidate's assembly is one the panel reserves or one a composed module already uses.</summary>
    /// <param name="candidate">The packaged module.</param>
    /// <param name="composed">The modules composed so far.</param>
    /// <returns>True when the candidate would share an assembly it may not.</returns>
    private static bool UsesReservedAssembly(IPanelModule candidate, List<IPanelModule> composed)
    {
        var assembly = candidate.GetType().Assembly;

        return IsReservedAssemblyName(assembly.GetName().Name)
            || composed.Any(module => { return module.GetType().Assembly == assembly; });
    }

    /// <summary>Runs the agent-capability guard on one candidate and turns its verdict into a refusal.</summary>
    /// <param name="candidate">The packaged module.</param>
    /// <returns>The refusal, or null when the candidate's dependencies are all declared.</returns>
    private static Error? JudgeCapabilities(IPanelModule candidate)
    {
        try
        {
            AgentCapabilityGuard.Verify([candidate]);
            return null;
        }
        catch (InvalidOperationException)
        {
            // The guard throws for the shipped modules, where a violation must fail the build; here the
            // same verdict removes one module. The exception's text names types and is not shown.
            return ModuleRefusals.AgentCapabilityUndeclared;
        }
        catch (ReflectionTypeLoadException)
        {
            // The guard reads every type of the assembly; one it cannot load means the assembly is not
            // what its manifest claimed, or was built against a contract this panel does not have.
            return ModuleRefusals.AssemblyUnreadable;
        }
    }

    /// <summary>Whether adding a manifest to the composed set breaks a rule the set did not already break.</summary>
    /// <param name="manifest">The candidate's manifest.</param>
    /// <param name="composed">The modules composed so far.</param>
    /// <param name="panel">The panel's version.</param>
    /// <returns>True when the candidate introduces at least one finding.</returns>
    private static bool BreaksContract(Manifest manifest, List<IPanelModule> composed, SemanticVersion panel)
    {
        var before = composed.Select(module => { return module.Manifest; }).ToList();
        var after = before.Append(manifest).ToList();

        var existing = ManifestValidator.ValidateSet(before, panel, ManifestPermissionVocabulary.Policies).ToHashSet();

        return ManifestValidator.ValidateSet(after, panel, ManifestPermissionVocabulary.Policies)
            .Any(finding => { return !existing.Contains(finding); });
    }
}
