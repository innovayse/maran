using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.ServerMigration.Commands.IssueExportGrant;

/// <summary>
/// Handles <see cref="IssueExportGrantCommand"/>: mints the token, stores its digest and returns the token once
/// (issue #127).
/// </summary>
/// <remarks>
/// Only the digest is stored. The token reaches the administrator in this answer and exists nowhere else; the
/// journal line names the export and never the token. An export that has expired is answered as not found, so a
/// grant is never issued for a package that would not be served.
/// </remarks>
public sealed class IssueExportGrantCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the issue.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the issue.</param>
    public IssueExportGrantCommandHandler(ServerMigrationDbContext dbContext, IClock clock, ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Issues the grant.</summary>
    /// <param name="command">The export and the grant's bounds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The grant with its token, shown once; or the refusal.</returns>
    public async Task<Result<ExportGrantDto>> HandleAsync(IssueExportGrantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;
        var export = await _dbContext.AccountExports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.ExportId, cancellationToken);

        if (export is null || !export.IsAvailable(now))
        {
            await _journal.RecordFailureAsync(
                MigrationAuditActions.ExportGrantIssued, command.ExportId.ToString(), command.IpAddress, command.UserAgent, cancellationToken);

            return Result<ExportGrantDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationExportNotFound), ErrorType.NotFound));
        }

        var lifetime = command.LifetimeMinutes is { } minutes
            ? TimeSpan.FromMinutes(minutes)
            : MigrationPackageLimits.DefaultGrantLifetime;
        var allowedFrom = AllowedFromPolicy.Require(command.AllowedFrom);
        var (token, digest) = ExportGrantTokenHasher.Mint();
        var grant = new ExportGrant(Guid.NewGuid(), export.Id, digest, allowedFrom, now, lifetime);
        _dbContext.ExportGrants.Add(grant);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ExportGrantIssued, export.SourceAccountName, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ExportGrantDto>.Ok(
            new ExportGrantDto(grant.Id, export.Id, new SensitiveString(token), grant.AllowedFrom, grant.ExpiresAt));
    }
}
