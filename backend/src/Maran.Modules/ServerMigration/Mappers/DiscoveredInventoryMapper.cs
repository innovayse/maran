using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>Restates what the agent read from a source as the rows the registry stores for the server.</summary>
public static class DiscoveredInventoryMapper
{
    /// <summary>Restates one account with its sites and databases.</summary>
    /// <param name="serverId">The server the account was found on.</param>
    /// <param name="source">The account as the agent reported it.</param>
    /// <returns>The account row, with its sites and databases.</returns>
    public static DiscoveredAccount ToAccount(Guid serverId, AgentSourceAccount source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var account = new DiscoveredAccount(
            Guid.NewGuid(),
            serverId,
            source.Name,
            source.Home,
            source.DiskBytes,
            source.CronEntryCount,
            source.MailboxCount);

        foreach (var site in source.Sites)
        {
            account.AddSite(
                Guid.NewGuid(),
                site.Domain,
                site.Aliases,
                BackendOf(site.Backend),
                site.PhpVersion,
                site.DocumentRoot);
        }

        foreach (var database in source.Databases)
        {
            account.AddDatabase(Guid.NewGuid(), database.Name, database.SizeBytes);
        }

        return account;
    }

    /// <summary>Restates one warning.</summary>
    /// <param name="serverId">The server the warning is about.</param>
    /// <param name="source">The warning as the agent reported it.</param>
    /// <returns>The warning row.</returns>
    public static DiscoveryWarning ToWarning(Guid serverId, AgentSourceWarning source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DiscoveryWarning(Guid.NewGuid(), serverId, WarningKindOf(source.Kind), source.Subject);
    }

    /// <summary>Restates how a site is served.</summary>
    /// <param name="backend">The agent's backend.</param>
    /// <returns>This module's backend.</returns>
    private static DiscoveredSiteBackend BackendOf(SourceSiteBackend backend)
    {
        return backend switch
        {
            SourceSiteBackend.Unknown => DiscoveredSiteBackend.Unknown,
            SourceSiteBackend.Static => DiscoveredSiteBackend.Static,
            SourceSiteBackend.Php => DiscoveredSiteBackend.Php,
            SourceSiteBackend.Proxy => DiscoveredSiteBackend.Proxy,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown site backend."),
        };
    }

    /// <summary>Restates the kind of a warning.</summary>
    /// <param name="kind">The agent's kind.</param>
    /// <returns>This module's kind.</returns>
    private static DiscoveryWarningKind WarningKindOf(SourceWarningKind kind)
    {
        return kind switch
        {
            SourceWarningKind.NameRefused => DiscoveryWarningKind.NameRefused,
            SourceWarningKind.SiteUnreadable => DiscoveryWarningKind.SiteUnreadable,
            SourceWarningKind.CronUnreadable => DiscoveryWarningKind.CronUnreadable,
            SourceWarningKind.DatabasesUnreadable => DiscoveryWarningKind.DatabasesUnreadable,
            SourceWarningKind.DiskUnreadable => DiscoveryWarningKind.DiskUnreadable,
            SourceWarningKind.Truncated => DiscoveryWarningKind.Truncated,
            SourceWarningKind.MailNotMigrated => DiscoveryWarningKind.MailNotMigrated,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown warning kind."),
        };
    }
}
