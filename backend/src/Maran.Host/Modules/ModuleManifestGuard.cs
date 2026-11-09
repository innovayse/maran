using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Manifests;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Host.Modules;

/// <summary>
/// Refuses to compose a set of modules whose manifests break a rule of the module contract: a
/// dependency that is missing, outside its range or part of a loop, a version the panel cannot read,
/// a module that needs a newer panel, a permission outside the policy vocabulary, a duplicate id,
/// menu key or route name.
/// </summary>
/// <remarks>
/// <para>
/// The rules themselves are <see cref="ManifestValidator"/>'s, in the Sdk, because the loader judges a
/// downloaded module by the same ones; this type is only the composition root's use of them. It is
/// here, and not in the Sdk, because only the Host sees every composed module at once and knows which
/// panel they are composed into (<see cref="IPanelVersion"/>).
/// </para>
/// <para>
/// It throws, like <see cref="AgentCapabilityGuard"/>: the answer is available before the first
/// request, and a panel composed from a broken set is worse than none, since a module would start
/// against a neighbour that is absent or of a version it was never written for. Every finding is
/// collected first so one run names all of them.
/// </para>
/// </remarks>
public static class ModuleManifestGuard
{
    /// <summary>Verifies that the composed manifests satisfy the module contract.</summary>
    /// <param name="manifests">The manifests of the modules about to be composed.</param>
    /// <param name="panelVersion">The version of the panel they are composed into.</param>
    /// <exception cref="InvalidOperationException">
    /// The panel's own version is not a semantic version, or at least one manifest breaks a rule. The
    /// message names every finding, sorted, so the same set always fails with the same text.
    /// </exception>
    public static void Verify(IReadOnlyList<Manifest> manifests, IPanelVersion panelVersion)
    {
        if (!SemanticVersion.TryParse(panelVersion.Current, out var panel))
        {
            throw new InvalidOperationException(
                $"The panel's own version '{panelVersion.Current}' is not a semantic version, so no module's "
                + "minimum panel version can be compared with it. Refusing to compose any module.");
        }

        var findings = ManifestValidator.ValidateSet(manifests, panel!, ManifestPermissionVocabulary.Policies)
            .Select(finding => { return $"{finding.ModuleId}: {finding.Kind} ({finding.Subject})"; })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(finding => { return finding; }, StringComparer.Ordinal)
            .ToList();

        if (findings.Count > 0)
        {
            throw new InvalidOperationException(
                "These module manifests break the module contract: " + string.Join("; ", findings)
                + ". Fix the manifest, or compose the module it depends on.");
        }
    }
}
