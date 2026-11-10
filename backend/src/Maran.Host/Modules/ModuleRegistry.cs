using Maran.Modules.Accounts;
using Maran.Modules.Apps;
using Maran.Modules.Backups;
using Maran.Modules.Cron;
using Maran.Modules.Databases;
using Maran.Modules.Dns;
using Maran.Modules.Files;
using Maran.Modules.Firewall;
using Maran.Modules.Ftp;
using Maran.Modules.Idempotency;
using Maran.Modules.Identity;
using Maran.Modules.Licensing;
using Maran.Modules.Mail;
using Maran.Modules.Marketplace;
using Maran.Modules.Monitoring;
using Maran.Modules.Notifications;
using Maran.Modules.ServerMigration;
using Maran.Modules.Sftp;
using Maran.Modules.Sites;
using Maran.Modules.Ssl;
using Maran.Modules.Tasks;
using Maran.Modules.Updates;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>Explicit registry of compiled-in modules (plans 2+ add entries).</summary>
public static class ModuleRegistry
{
    /// <summary>
    /// All modules in load order. Deliberately explicit — no assembly scanning. Identity comes
    /// first: it owns who may sign in, so every other module's endpoints are meaningless until
    /// its services are registered.
    ///
    /// Order is about SERVICE REGISTRATION ONLY, not resolution: IServiceCollection resolves by
    /// type regardless of the order things were added, so Sites listed after Accounts is a reading
    /// convenience (a site belongs to an account, a certificate to a site) and not a dependency the
    /// container enforces.
    /// </summary>
    public static IReadOnlyList<IPanelModule> All { get; } =
        [
            new IdentityModule(),

            // Licensing is here since it has a screen (Settings, Licence). Its start-up check is a hosted
            // service the Host adds (Program.cs, AddLicensingStartupCheck), because a module cannot
            // register one. Registration order is a reading convenience, but a reviewer looks for
            // licensing early.
            new LicensingModule(),

            new AccountsModule(),
            new SitesModule(),

            // The app store (issue #517): the web server and runtimes sites run on, and the first-run gate
            // the Sites module reads. Administrator-facing, one endpoint readable by every signed-in role.
            new AppsModule(),
            new SslModule(),
            new DatabasesModule(),
            new SftpModule(),
            new FtpModule(),
            new MailModule(),
            new DnsModule(),
            new CronModule(),
            new FilesModule(),
            new FirewallModule(),
            new TasksModule(),
            new MonitoringModule(),
            new NotificationsModule(),
            new BackupsModule(),
            new IdempotencyModule(),
            // The panel's own update screen (issue #382): administrator only, no menu entry because the
            // SPA reaches it from Settings, and no schema.
            new UpdatesModule(),

            // The registry of remote servers an administrator reads from (issue #197): administrator only,
            // the only module that drives the agent's outbound SSH connection.
            new ServerMigrationModule(),

            // The way a panel gets further modules (issues #186, #187): administrator only, no menu entry
            // because the SPA reaches it from Settings, no agent capability, and inert until an
            // administrator switches it on - it makes no call of its own at start-up.
            new MarketplaceModule(),
        ];
}
