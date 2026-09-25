using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts;

/// <summary>
/// Holds the Accounts module's single published <see cref="Manifest"/> instance
/// (rules/csharp.md "Canonical backend layout" — module identity). Kept as its own type, distinct
/// from <see cref="AccountsModule"/>, because <see cref="Manifest"/> is plain data with no
/// framework dependency, while <see cref="AccountsModule"/> is the DI/routing entry point.
/// </summary>
public static class AccountsManifest
{
    /// <summary>The Accounts module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleAudience.AdministratorOnly"/> even though a signed-in customer reads their
    /// own account through <c>GET /api/v1/accounts/me</c>: that route is served by the separate
    /// <c>MyAccountController</c>, not by this module's landing screen, and the catalogue is
    /// describing the latter. This module's own screens — creating, listing and managing OTHER
    /// accounts — are an administrator's job; a customer never needs a navigation entry for it.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "accounts",
        DisplayNameKey: "AccountsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Accounts],
        Audience: ModuleAudience.AdministratorOnly);
}
