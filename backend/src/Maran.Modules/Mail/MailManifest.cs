using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Mail;

/// <summary>
/// Holds the Mail module's single published <see cref="Manifest"/> instance (rules/csharp.md "Canonical
/// backend layout" — module identity). Kept as its own type, distinct from <see cref="MailModule"/>, because
/// <see cref="Manifest"/> is plain data with no framework dependency, while <see cref="MailModule"/> is the
/// DI entry point.
/// </summary>
public static class MailManifest
{
    /// <summary>The Mail module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="LicenceTier.Included"/> now, as the one constant a later paid decision changes
    /// (decision D14 of the mail plan): nothing else in the module names a tier.
    /// </para>
    /// <para>
    /// <see cref="AgentCapability.Mail"/> and NOTHING else, which an administrator reads before installing and
    /// <c>AgentCapabilityGuard</c> refuses to compose past. In particular NOT <see cref="AgentCapability.Firewall"/>:
    /// this module does not open the mail ports, it reports which ports an operator would have to open. And NOT
    /// <see cref="AgentCapability.Ssl"/>: the daemons use the panel's own certificate, which the agent reads
    /// from its fixed place; nothing here writes certificate material.
    /// </para>
    /// <para>
    /// <see cref="ModuleKind.Packageable"/>: hosted mail is an optional server feature, not part of the core.
    /// <see cref="Manifest.SchemaName"/> is the <c>mail</c> schema. The three lists are open to everyone, and
    /// the account's plan feature switches them off per account (<see cref="AccountFeature.Mail"/>). The
    /// manifest names exactly the screens the SPA router has: the administrator's server page is not listed
    /// until it exists (a menu entry or route the router lacks is a dead link).
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "mail",
        DisplayNameKey: "MailModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "mail",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Mail],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "mail-domains",
                RouteName: "mail-domains",
                IconKey: ModuleIconKeys.Mail,
                LabelKey: "MailMenuDomains",
                Order: 10,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "mailboxes",
                RouteName: "mailboxes",
                IconKey: ModuleIconKeys.Mail,
                LabelKey: "MailMenuMailboxes",
                Order: 20,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "mail-aliases",
                RouteName: "mail-aliases",
                IconKey: ModuleIconKeys.Mail,
                LabelKey: "MailMenuAliases",
                Order: 30,
                Audience: ModuleAudience.Everyone),
        ],
        Routes: [
            new ModuleRoute(
                Name: "mail-domains",
                Path: "/mail/domains",
                TitleKey: "MailRouteDomains",
                RequiresModule: null),
            new ModuleRoute(
                Name: "mailboxes",
                Path: "/mail/mailboxes",
                TitleKey: "MailRouteMailboxes",
                RequiresModule: null),
            new ModuleRoute(
                Name: "mail-aliases",
                Path: "/mail/aliases",
                TitleKey: "MailRouteAliases",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
