using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

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
    /// <see cref="ModuleAudience.AdminOrReseller"/>: this module's own screens — creating, listing and
    /// managing hosting accounts and the plans they are sold on — are the job of whoever sells hosting,
    /// and that is an administrator (the whole server) or a reseller (what it owns, narrowed by the
    /// module's own query filters, never by the catalogue). A customer is not offered them even though
    /// a signed-in customer reads their own account through <c>GET /api/v1/accounts/me</c>: that route
    /// is served by the separate <c>MyAccountController</c>, not by this module's landing screen, and
    /// the catalogue is describing the latter.
    /// <para>
    /// <see cref="ModuleKind.Core"/>: tenancy and plans, which every other module reads through the Sdk's account
    /// windows. <see cref="Manifest.SchemaName"/> is the <c>accounts</c> schema. <see cref="Manifest.Permissions"/>
    /// includes the four reseller-key scopes, because the module's external controllers declare them. The
    /// <c>reseller-overview</c> entry is for a reseller's own login, which <see cref="ModuleAudience"/> has no value
    /// for; it is listed as <see cref="ModuleAudience.AdminOrReseller"/> (the nearest, and permissive) and the
    /// endpoints behind it enforce the role on their own.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "accounts",
        DisplayNameKey: "AccountsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "accounts",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Accounts],
        Permissions: [
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.AdminOrReseller,
            AuthorizationPolicies.AnyAuthenticated,
            AuthorizationPolicies.ResellerOnly,
            ApiKeyPolicyNames.AccountsDelete,
            ApiKeyPolicyNames.AccountsRead,
            ApiKeyPolicyNames.AccountsWrite,
            ApiKeyPolicyNames.PlansRead],
        Menu: [
            new ModuleMenuItem(
                Key: "accounts-list",
                RouteName: "accounts",
                IconKey: ModuleIconKeys.Users,
                LabelKey: "AccountsMenuAccountsList",
                Order: 10,
                Audience: ModuleAudience.AdminOrReseller),
            new ModuleMenuItem(
                Key: "accounts-new",
                RouteName: "accounts-new",
                IconKey: ModuleIconKeys.Users,
                LabelKey: "AccountsMenuAccountsNew",
                Order: 20,
                Audience: ModuleAudience.AdminOrReseller),
            new ModuleMenuItem(
                Key: "plans",
                RouteName: "plans",
                IconKey: ModuleIconKeys.Users,
                LabelKey: "AccountsMenuPlans",
                Order: 30,
                Audience: ModuleAudience.AdminOrReseller),
            new ModuleMenuItem(
                Key: "resellers",
                RouteName: "resellers",
                IconKey: ModuleIconKeys.Users,
                LabelKey: "AccountsMenuResellers",
                Order: 40,
                Audience: ModuleAudience.AdministratorOnly),
            new ModuleMenuItem(
                Key: "reseller-overview",
                RouteName: "reseller-overview",
                IconKey: ModuleIconKeys.House,
                LabelKey: "AccountsMenuResellerOverview",
                Order: 50,
                Audience: ModuleAudience.AdminOrReseller),
        ],
        Routes: [
            new ModuleRoute(
                Name: "accounts",
                Path: "/accounts",
                TitleKey: "AccountsRouteAccounts",
                RequiresModule: null),
            new ModuleRoute(
                Name: "accounts-new",
                Path: "/accounts/new",
                TitleKey: "AccountsRouteAccountsNew",
                RequiresModule: null),
            new ModuleRoute(
                Name: "account-detail",
                Path: "/accounts/:id",
                TitleKey: "AccountsRouteAccountDetail",
                RequiresModule: null),
            new ModuleRoute(
                Name: "plans",
                Path: "/plans",
                TitleKey: "AccountsRoutePlans",
                RequiresModule: null),
            new ModuleRoute(
                Name: "plans-new",
                Path: "/plans/new",
                TitleKey: "AccountsRoutePlansNew",
                RequiresModule: null),
            new ModuleRoute(
                Name: "plans-edit",
                Path: "/plans/:id",
                TitleKey: "AccountsRoutePlansEdit",
                RequiresModule: null),
            new ModuleRoute(
                Name: "resellers",
                Path: "/resellers",
                TitleKey: "AccountsRouteResellers",
                RequiresModule: null),
            new ModuleRoute(
                Name: "resellers-new",
                Path: "/resellers/new",
                TitleKey: "AccountsRouteResellersNew",
                RequiresModule: null),
            new ModuleRoute(
                Name: "reseller-detail",
                Path: "/resellers/:id",
                TitleKey: "AccountsRouteResellerDetail",
                RequiresModule: null),
            new ModuleRoute(
                Name: "reseller-profile-edit",
                Path: "/resellers/:id/profile",
                TitleKey: "AccountsRouteResellerProfileEdit",
                RequiresModule: null),
            new ModuleRoute(
                Name: "reseller-edit",
                Path: "/resellers/:id/edit",
                TitleKey: "AccountsRouteResellerEdit",
                RequiresModule: null),
            new ModuleRoute(
                Name: "reseller-api-keys-oversight",
                Path: "/resellers/:id/api-keys",
                TitleKey: "AccountsRouteResellerApiKeysOversight",
                RequiresModule: null),
            new ModuleRoute(
                Name: "reseller-overview",
                Path: "/reseller",
                TitleKey: "AccountsRouteResellerOverview",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.AdminOrReseller);
}
