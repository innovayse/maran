using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Extensions;

/// <summary>
/// What every operation on a registered remote server shares: finding the server with its host keys, reading its
/// warnings, and discarding what a discovery stored.
/// </summary>
public static class ServerMigrationDbContextExtensions
{
    /// <summary>Finds a registered server for change, with the host keys it last offered.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="serverId">The server.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tracked server, or <c>null</c> when none is registered under that identifier.</returns>
    public static async Task<RemoteServer?> FindServerAsync(
        this ServerMigrationDbContext dbContext,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return await dbContext.RemoteServers
            .Include(server => server.HostKeyCandidates)
            .FirstOrDefaultAsync(server => server.Id == serverId, cancellationToken);
    }

    /// <summary>Reads the warnings the server's last discovery raised, worded for the caller.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="serverId">The server.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The warnings in a stable order; empty when none was raised.</returns>
    public static async Task<IReadOnlyList<DiscoveryWarningDto>> ReadWarningsAsync(
        this ServerMigrationDbContext dbContext,
        Guid serverId,
        IStringLocalizer<DisplayNames> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(names);

        var warnings = await dbContext.DiscoveryWarnings
            .AsNoTracking()
            .Where(warning => warning.RemoteServerId == serverId)
            .OrderBy(warning => warning.Kind)
            .ThenBy(warning => warning.Subject)
            .ToListAsync(cancellationToken);

        return warnings
            .Select(warning =>
            {
                return DiscoveryWarningMapper.From(warning, names);
            })
            .ToList();
    }

    /// <summary>
    /// Marks everything a server's discovery stored for deletion: its accounts with their sites and databases, and
    /// its warnings. The caller saves, so the removal and whatever replaces it are one transaction.
    /// </summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="serverId">The server.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A task that completes when the rows are marked.</returns>
    /// <remarks>
    /// The graph is loaded and removed through the context rather than deleted in one statement, so the same code
    /// removes the same rows on every provider and the database's cascade is a second guarantee, not the only one.
    /// A discovery is bounded (<c>DiscoveryLimits</c>), so the graph is too.
    /// </remarks>
    public static async Task DiscardDiscoveryAsync(
        this ServerMigrationDbContext dbContext,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var accounts = await dbContext.DiscoveredAccounts
            .Include(account => account.Sites)
            .Include(account => account.Databases)
            .Where(account => account.RemoteServerId == serverId)
            .ToListAsync(cancellationToken);
        dbContext.DiscoveredAccounts.RemoveRange(accounts);

        var warnings = await dbContext.DiscoveryWarnings
            .Where(warning => warning.RemoteServerId == serverId)
            .ToListAsync(cancellationToken);
        dbContext.DiscoveryWarnings.RemoveRange(warnings);
    }
}
