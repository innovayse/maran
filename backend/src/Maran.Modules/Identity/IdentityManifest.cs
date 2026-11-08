using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Identity;

/// <summary>
/// Holds the Identity module's single published <see cref="Manifest"/> instance
/// (rules/csharp.md "Canonical backend layout" — module identity). Kept as its own type, distinct
/// from <see cref="IdentityModule"/>, because <see cref="Manifest"/> is plain data with no
/// framework dependency, while <see cref="IdentityModule"/> is the DI/routing entry point.
/// </summary>
public static class IdentityManifest
{
    /// <summary>The Identity module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Core"/>: every other module's actor, session and audit trail comes from here, so
    /// the panel is not usable with it removed. <see cref="Manifest.SchemaName"/> is the <c>identity</c> schema.
    /// <see cref="Manifest.Permissions"/> lists the policies its controllers declare; the sign-in, password
    /// reset and invitation endpoints are anonymous and declare none. Only the API-keys screen belongs to the
    /// licensed module: sessions, two-step verification and the profile are the signed-in person's own screens
    /// and carry no module gate, so they are not routes here.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "identity",
        DisplayNameKey: "IdentityModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "identity",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.AdminOrReseller,
            AuthorizationPolicies.AnyAuthenticated,
            AuthorizationPolicies.ResellerOnly,
            ApiKeyPolicyNames.SelfRead],
        Menu: [
            new ModuleMenuItem(
                Key: "api-keys",
                RouteName: "api-keys",
                IconKey: ModuleIconKeys.ShieldCheck,
                LabelKey: "IdentityMenuApiKeys",
                Order: 10,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "api-keys",
                Path: "/settings/api-keys",
                TitleKey: "IdentityRouteApiKeys",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
