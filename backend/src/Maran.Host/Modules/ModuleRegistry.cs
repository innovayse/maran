using Maran.Modules.Accounts;
using Maran.Modules.Databases;
using Maran.Modules.Identity;
using Maran.Modules.Sftp;
using Maran.Modules.Sites;
using Maran.Modules.Ssl;
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
            new AccountsModule(),
            new SitesModule(),
            new SslModule(),
            new DatabasesModule(),
            new SftpModule(),
        ];
}
