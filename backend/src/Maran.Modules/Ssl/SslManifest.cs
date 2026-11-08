using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl;

/// <summary>
/// Holds the Ssl module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="SslModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="SslModule"/> is the DI/routing entry point.
/// </summary>
public static class SslManifest
{
    /// <summary>The Ssl module's published identity.</summary>
    /// <remarks>
    /// Depends on <c>sites</c>: a certificate is installed for a site, and every operation here reads
    /// a site through the Sdk's site directory. The dependency is declared here — where the module
    /// catalogue can see it — and is NOT a project reference, which the architecture tests forbid.
    /// <para>
    /// <see cref="ModuleKind.Packageable"/>: a panel without certificates still serves sites.
    /// <see cref="Manifest.SchemaName"/>
    /// is the <c>ssl</c> schema, and the dependency on <c>sites</c> carries the major range this module was
    /// written against. It declares no menu entry and no route of its own: a certificate is a tab on the site it
    /// protects, so its screens belong to the Sites routes.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "ssl",
        DisplayNameKey: "SslModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "ssl",
        Dependencies: [new ModuleDependency("sites", ">=1.0.0 <2.0.0")],
        AgentCapabilities: [AgentCapability.Files, AgentCapability.Sites, AgentCapability.Ssl],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.Everyone);
}
