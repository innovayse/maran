using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace;

/// <summary>
/// Holds the Marketplace module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="MarketplaceModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while the module is the DI entry point.
/// </summary>
public static class MarketplaceManifest
{
    /// <summary>The Marketplace module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="ModuleKind.Core"/> and <see cref="LicenceTier.Included"/>: the way a panel gets further
    /// modules is not itself a module a licence can switch off, and it is the administrator-only door the
    /// rest of the product's packaged modules arrive through. <see cref="ModuleAudience.AdministratorOnly"/>:
    /// no customer, reseller or API key ever sees it offered.
    /// </para>
    /// <para>
    /// <b>No agent capability.</b> It never touches the agent: it downloads a package into a panel-owned
    /// directory and hands it to the loader, and the loader is not the agent. Nothing downloaded ever
    /// reaches the root process (rules/security.md items 12 and 13). Its one schema is <c>marketplace</c>.
    /// </para>
    /// <para>
    /// <b>No menu entry and no route of its own, and that is a decision.</b> Its screen is a page of the
    /// core SPA reached from Settings, the shape the Updates module has: <c>navigationTree.ts</c> lists the
    /// Settings item (<c>module-landing-coverage.spec.ts</c> follows the tree for a module the tree names),
    /// and a manifest menu entry would be a second door to one room. A module in the registry is a promise
    /// of a destination; this one keeps it through the tree, and is added to the registry in the same
    /// change that adds its screen.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "marketplace",
        DisplayNameKey: "MarketplaceModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "marketplace",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [AuthorizationPolicies.AdminOnly],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.AdministratorOnly);
}
