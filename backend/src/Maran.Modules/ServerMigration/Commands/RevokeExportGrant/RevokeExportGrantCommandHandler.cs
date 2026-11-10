using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Commands.RevokeExportGrant;

/// <summary>Handles <see cref="RevokeExportGrantCommand"/>: ends the grant, whatever state it is in (issue #127).</summary>
/// <remarks>Revoking is idempotent: a grant already revoked, used or expired is answered with success and keeps the time it first ended.</remarks>
public sealed class RevokeExportGrantCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the revocation.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the revocation.</param>
    public RevokeExportGrantCommandHandler(ServerMigrationDbContext dbContext, IClock clock, ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Revokes the grant.</summary>
    /// <param name="command">The grant.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or not found.</returns>
    public async Task<Result> HandleAsync(RevokeExportGrantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var grant = await _dbContext.ExportGrants.FirstOrDefaultAsync(row => row.Id == command.GrantId, cancellationToken);

        if (grant is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationGrantNotFound), ErrorType.NotFound));
        }

        grant.Revoke(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ExportGrantRevoked, grant.ExportId.ToString(), command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }
}
