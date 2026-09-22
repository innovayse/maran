using Maran.Modules.Accounts;
using Maran.Modules.Backups;
using Maran.Modules.Cron;
using Maran.Modules.Databases;
using Maran.Modules.Firewall;
using Maran.Modules.Ftp;
using Maran.Modules.Identity;
using Maran.Modules.Monitoring;
using Maran.Modules.Notifications;
using Maran.Modules.Sftp;
using Maran.Modules.Sites;
using Maran.Modules.Ssl;
using Maran.Modules.Tasks;
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

            // LICENSING IS DELIBERATELY NOT HERE, and this note is why rather than an oversight.
            // It was registered second for a while — right after Identity, before every module its
            // status could one day gate — and the SPA's own coverage check went red on it:
            // "every module the backend composes has a sidebar destination that is not the upgrade
            // wall". That check is right. A module in this list is a promise to the interface, and
            // licence verification has no screen yet, so the promise could not be kept: an operator
            // would have had a module they could never reach. It is wired instead as a hosted
            // service (Program.cs, AddLicensingStartupCheck), which is what actually needs to run.
            // When it grows a screen, it belongs here — and the ordering argument still holds then:
            // registration order is a reading convenience, but a reviewer looks for licensing early.

            new AccountsModule(),
            new SitesModule(),
            new SslModule(),
            new DatabasesModule(),
            new SftpModule(),
            new FtpModule(),
            new CronModule(),
            new FirewallModule(),
            new TasksModule(),
            new MonitoringModule(),
            new NotificationsModule(),
            new BackupsModule(),
        ];
}
