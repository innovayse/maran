using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.ListRemoteServers;

/// <summary>Reads one page of the registered remote servers.</summary>
/// <remarks>
/// A server carries no account and is the administrator's own bookkeeping, so the read is not tenant-scoped and the
/// surface that reaches it is administrator-only. Search, filter and order all run in the database, so the total
/// counts the servers that matched and not the table. The list shape holds no key material of any kind.
/// </remarks>
public sealed class ListRemoteServersQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, which name each server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="names">The module's display names.</param>
    public ListRemoteServersQueryHandler(ServerMigrationDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Narrows by search and state, and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<RemoteServerDto>>> HandleAsync(
        ListRemoteServersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<RemoteServer> servers = _dbContext.RemoteServers.AsNoTracking();

        if (!query.State.IsNone)
        {
            servers = StateOf(query.State.Value) is { } state
                ? servers.Where(server => server.State == state)
                : servers.Where(_ => false);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so the
            // term matches literally. Either the label or the address answers a search for it.
            var pattern = LikePattern.Contains(query.Search.Value);
            servers = servers.Where(server =>
                EF.Functions.ILike(server.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(server.Host, pattern, LikePattern.EscapeCharacter));
        }

        var page = await servers.ToPagedAsync(query, ListRemoteServersSorts.Map, cancellationToken);

        return Result<PagedResult<RemoteServerDto>>.Ok(page.Map(server =>
        {
            return RemoteServerMapper.ToDto(server, _names);
        }));
    }

    /// <summary>Reads a state filter spelled as a state's name.</summary>
    /// <param name="spelling">The filter value.</param>
    /// <returns>The state, or <c>null</c> when the spelling is no state's name. A number is not a name.</returns>
    private static RemoteServerState? StateOf(string spelling)
    {
        foreach (var state in Enum.GetValues<RemoteServerState>())
        {
            if (string.Equals(state.ToString(), spelling, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }
}
