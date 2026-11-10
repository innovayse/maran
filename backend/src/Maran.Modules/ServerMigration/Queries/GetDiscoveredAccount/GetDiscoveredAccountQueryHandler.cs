using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.GetDiscoveredAccount;

/// <summary>Reads one discovered account in full.</summary>
/// <remarks>
/// The account is found by BOTH identifiers: an account of another server answers as not found under this one, so a
/// route that pairs one server's id with another server's account leaks nothing and confirms nothing.
/// </remarks>
public sealed class GetDiscoveredAccountQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, which name how each site is served in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="names">The module's display names.</param>
    public GetDiscoveredAccountQueryHandler(ServerMigrationDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Reads the account.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The account with its sites and databases, or <c>RemoteServerNotFound</c> when no account of that server has
    /// that identifier.
    /// </returns>
    public async Task<Result<DiscoveredAccountDetailDto>> HandleAsync(
        GetDiscoveredAccountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await _dbContext.DiscoveredAccounts
            .AsNoTracking()
            .Include(row => row.Sites)
            .Include(row => row.Databases)
            .FirstOrDefaultAsync(
                row => row.Id == query.AccountId && row.RemoteServerId == query.ServerId,
                cancellationToken);
        if (account is null)
        {
            return Result<DiscoveredAccountDetailDto>.Fail(
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound));
        }

        return Result<DiscoveredAccountDetailDto>.Ok(DiscoveredAccountMapper.ToDetail(account, _names));
    }
}
