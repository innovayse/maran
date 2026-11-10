using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Persistence;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Spends a grant under a row lock, so that two requests presenting the same grant at the same moment cannot both
/// take the single read of the artifact (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule stays on the entity.</b> <see cref="ExportGrant.ReadManifest"/> and
/// <see cref="ExportGrant.ConsumeArtifact"/> decide; this class only makes the read-decide-write one critical
/// section. Without the lock two requests could both load a grant with <c>ArtifactConsumedAt</c> empty and both be
/// told yes, which is the single use the grant exists to promise.
/// </para>
/// <para>
/// The package is served when this says yes, never before: a refusal here is the whole of the answer and the
/// caller sees the same 404-shaped refusal whatever the reason.
/// </para>
/// </remarks>
public sealed class ExportGrantLedger : IExportGrantLedger
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the ledger.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public ExportGrantLedger(ServerMigrationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<bool> CountManifestReadAsync(Guid grantId, CancellationToken cancellationToken)
    {
        return await SpendAsync(
            grantId,
            (grant, now) =>
            {
                return grant.ReadManifest(now);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TakeArtifactReadAsync(Guid grantId, CancellationToken cancellationToken)
    {
        return await SpendAsync(
            grantId,
            (grant, now) =>
            {
                return grant.ConsumeArtifact(now);
            },
            cancellationToken);
    }

    /// <summary>Locks the grant's row, applies a rule to it and saves when the rule said yes.</summary>
    /// <param name="grantId">The grant.</param>
    /// <param name="rule">The entity's rule: true when the read is allowed (and has changed the entity).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the rule answered, or <c>false</c> when the grant no longer exists.</returns>
    private async Task<bool> SpendAsync(
        Guid grantId,
        Func<ExportGrant, DateTimeOffset, bool> rule,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw-sql: a row lock (FOR UPDATE) has no LINQ translation and no EF Core equivalent.
        // Parameterized through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"): the grant id is a
        // parameter, and the only literals are the module's own schema and table, quoted because the names are
        // PascalCase.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""select 1 from servermigration."ExportGrants" where "Id" = {grantId} for update""",
            cancellationToken);

        var grant = await _dbContext.ExportGrants.FirstOrDefaultAsync(row => row.Id == grantId, cancellationToken);

        if (grant is null)
        {
            return false;
        }

        var allowed = rule(grant, _clock.UtcNow);

        if (allowed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return allowed;
    }
}
