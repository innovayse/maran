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
    public static Manifest Instance { get; } = new(
        Id: "accounts",
        DisplayNameKey: "ModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: []);
}
