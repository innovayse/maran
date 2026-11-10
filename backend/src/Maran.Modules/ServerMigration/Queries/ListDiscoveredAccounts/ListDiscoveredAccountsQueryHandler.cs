using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>Reads one page of the accounts a discovery found on one remote server.</summary>
/// <remarks>
/// <para>
/// The server is established first, so an identifier that names none is <c>RemoteServerNotFound</c> and not an empty
/// page that would look like a server with no accounts. Search, filter and order all run in the database, over the
/// login and the space-joined domains the row stores for exactly this purpose, so the total counts the accounts that
/// matched and not the table.
/// </para>
/// <para>
/// A server carries no account and the read is not tenant-scoped; the surface is administrator-only.
/// </para>
/// </remarks>
public sealed class ListDiscoveredAccountsQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    public ListDiscoveredAccountsQueryHandler(ServerMigrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Narrows by search and by the thing the accounts have, and answers the requested page.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or <c>RemoteServerNotFound</c>.</returns>
    public async Task<Result<PagedResult<DiscoveredAccountDto>>> HandleAsync(
        ListDiscoveredAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var exists = await _dbContext.RemoteServers
            .AsNoTracking()
            .AnyAsync(server => server.Id == query.ServerId, cancellationToken);
        if (!exists)
        {
            return Result<PagedResult<DiscoveredAccountDto>>.Fail(
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound));
        }

        IQueryable<DiscoveredAccount> accounts = _dbContext.DiscoveredAccounts
            .AsNoTracking()
            .Where(account => account.RemoteServerId == query.ServerId);

        if (!query.Has.IsNone)
        {
            accounts = Having(accounts, query.Has.Value);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so the
            // term matches literally. The login or any domain the account's sites answer to answers a search.
            var pattern = LikePattern.Contains(query.Search.Value);
            accounts = accounts.Where(account =>
                EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(account.DomainsText, pattern, LikePattern.EscapeCharacter));
        }

        var page = await accounts.ToPagedAsync(query, ListDiscoveredAccountsSorts.Map, cancellationToken);

        return Result<PagedResult<DiscoveredAccountDto>>.Ok(page.Map(DiscoveredAccountMapper.ToDto));
    }

    /// <summary>Narrows the accounts to those that have at least one of the named thing.</summary>
    /// <param name="accounts">The accounts narrowed so far.</param>
    /// <param name="has">The filter value, one of <see cref="DiscoveredAccountHasFilters"/>.</param>
    /// <returns>The narrowed set; empty for a spelling that names none of them.</returns>
    private static IQueryable<DiscoveredAccount> Having(IQueryable<DiscoveredAccount> accounts, string has)
    {
        return has switch
        {
            DiscoveredAccountHasFilters.Sites => accounts.Where(account => account.SiteCount > 0),
            DiscoveredAccountHasFilters.Databases => accounts.Where(account => account.DatabaseCount > 0),
            DiscoveredAccountHasFilters.Cron => accounts.Where(account => account.CronEntryCount > 0),
            DiscoveredAccountHasFilters.Mail => accounts.Where(account => account.MailboxCount > 0),
            _ => accounts.Where(_ => false),
        };
    }
}
