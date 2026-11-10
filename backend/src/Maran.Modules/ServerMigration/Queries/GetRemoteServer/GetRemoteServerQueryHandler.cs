using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Queries.GetRemoteServer;

/// <summary>Reads one registered remote server, with the host keys it offered and what its last discovery warned of.</summary>
/// <remarks>
/// A server is the administrator's bookkeeping and carries no account, so there is no tenant boundary to cross; the
/// surface that reads it is administrator-only and a customer is refused at the controller. The private key is never
/// read into the answer: the detail carries the public key and the line to install.
/// </remarks>
public sealed class GetRemoteServerQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, which name the server's state in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="names">The module's display names.</param>
    public GetRemoteServerQueryHandler(ServerMigrationDbContext dbContext, IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Reads the server.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The server in full, or <c>RemoteServerNotFound</c>.</returns>
    public async Task<Result<RemoteServerDetailDto>> HandleAsync(
        GetRemoteServerQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var server = await _dbContext.RemoteServers
            .AsNoTracking()
            .Include(row => row.HostKeyCandidates)
            .FirstOrDefaultAsync(row => row.Id == query.ServerId, cancellationToken);
        if (server is null)
        {
            return Result<RemoteServerDetailDto>.Fail(
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound));
        }

        var warnings = await _dbContext.ReadWarningsAsync(server.Id, _names, cancellationToken);

        return Result<RemoteServerDetailDto>.Ok(RemoteServerMapper.ToDetail(server, warnings, _names));
    }
}
