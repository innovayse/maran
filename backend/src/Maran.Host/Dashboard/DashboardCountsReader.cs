using Maran.Modules.Accounts.Queries.CountAccounts;
using Maran.Modules.Databases.Queries.CountDatabases;
using Maran.Modules.Sites.Queries.CountSites;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Reads what this server holds — accounts, sites, databases — for the landing page and for the live
/// stream's <c>totals</c> event: the ONE place the three count queries are named.
/// </summary>
/// <remarks>
/// Each count comes from the module's own count query, never from its list: a paged listing holds one
/// page, not the total. A module that did not answer is unavailable, never zero. Authorization is the
/// caller's and comes first, exactly as for <see cref="AttentionReader"/>: these queries have no policy
/// of their own on the bus, so a caller confirms it is an administrator BEFORE calling this.
/// </remarks>
public static class DashboardCountsReader
{
    /// <summary>Reads the three counts together; one that cannot be read marks only itself.</summary>
    /// <param name="bus">The bus every module query is dispatched through.</param>
    /// <param name="logger">Where a count whose query threw is recorded.</param>
    /// <param name="cancellationToken">Cancels every dispatch.</param>
    /// <returns>The counts, each loaded or unavailable.</returns>
    public static async Task<DashboardCountsDto> ReadAsync(
        IMessageBus bus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var accounts = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountAccountsQuery(), cancellationToken);
        var sites = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountSitesQuery(), cancellationToken);
        var databases = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountDatabasesQuery(), cancellationToken);

        await Task.WhenAll(accounts, sites, databases);

        return new DashboardCountsDto(
            Accounts: await accounts,
            Sites: await sites,
            Databases: await databases);
    }
}
