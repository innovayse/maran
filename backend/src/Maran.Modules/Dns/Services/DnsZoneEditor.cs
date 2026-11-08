using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Models;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Npgsql;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// The one place a zone's content is changed on the host and in the database, so the order of the two and
/// the lock around them are written once: open the edit, change the zone through its methods, publish.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent is told first, the database second, and the database wins.</b> The zone is validated here,
/// the COMPLETE record set goes to the agent, and only when the agent reports it renders, checks and serves
/// does the row change. A refused edit therefore leaves the row, the file and the daemon as they were. The
/// window left open is a save that fails after the agent applied: the daemon then serves newer data than the
/// row. The start-up reconciler closes it (rules/architecture.md "Truth lives in PostgreSQL"), reissuing every
/// zone whose served serial differs from its row.
/// </para>
/// <para>
/// <b>One edit of one zone at a time.</b> Two overlapping edits would each start from the same records and
/// each publish a set missing the other's change, and the last write would win on the host while the first
/// won in the database. The edit takes a transaction-scoped advisory lock keyed by the zone, loads the zone
/// AFTER the lock, and holds the lock until commit. The serial's concurrency token is the second guard.
/// </para>
/// <para>
/// Whether the caller may touch the zone is decided before the lock is taken, from a read through the tenant
/// filter, so a caller can neither queue behind nor hold a lock on a zone it may not see.
/// </para>
/// </remarks>
public sealed class DnsZoneEditor
{
    /// <summary>The module's context, tenant-filtered.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Serialises everything that changes one zone.</summary>
    private readonly IDnsZoneLock _lock;

    /// <summary>Creates the editor.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="agent">The agent client that publishes the zone.</param>
    /// <param name="zoneLock">Serialises everything that changes one zone.</param>
    public DnsZoneEditor(DnsDbContext dbContext, IAgentDnsClient agent, IDnsZoneLock zoneLock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _lock = zoneLock;
    }

    /// <summary>Opens an edit of one zone the caller may see.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The open edit, or <c>ZoneNotFound</c> for a zone that does not exist or belongs to another account
    /// (the two are one answer), or <c>DnsNotEnabled</c> / <c>DnsSettingsIncomplete</c> when the name server
    /// is not switched on with its settings stated. Disposing the edit releases the lock.
    /// </returns>
    public async Task<Result<ZoneEdit>> OpenAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        var visible = await _dbContext.Zones.AsNoTracking().AnyAsync(zone => zone.Id == zoneId, cancellationToken);
        if (!visible)
        {
            return Result<ZoneEdit>.Fail(Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound));
        }

        var transaction = await _lock.TakeAsync(zoneId, cancellationToken);

        var zone = await _dbContext.Zones
            .Include(row => row.Records)
            .SingleOrDefaultAsync(row => row.Id == zoneId, cancellationToken);
        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);

        Error? refusal = null;
        if (zone is null)
        {
            refusal = Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound);
        }
        else if (settings is null || !settings.Enabled)
        {
            refusal = Error.Of(nameof(ErrorMessages.DnsNotEnabled), ErrorType.Conflict);
        }
        else if (!settings.IsComplete)
        {
            refusal = Error.Of(nameof(ErrorMessages.DnsSettingsIncomplete), ErrorType.Conflict);
        }

        if (refusal is not null || zone is null || settings is null)
        {
            await transaction.DisposeAsync();

            return Result<ZoneEdit>.Fail(refusal ?? Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound));
        }

        return Result<ZoneEdit>.Ok(new ZoneEdit(zone, settings, transaction));
    }

    /// <summary>Publishes the changed zone to the agent and, when it is served, saves it.</summary>
    /// <param name="edit">The open edit; its zone has been changed through the zone's own methods.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success; the agent's typed failure with nothing saved; or <c>ZoneChangedMeanwhile</c> when the save
    /// found the zone changed, which the lock makes a bug and not an expected path.
    /// </returns>
    public async Task<Result> PublishAsync(ZoneEdit edit, CancellationToken cancellationToken)
    {
        var published = await _agent.PutZoneAsync(
            ZoneDefinitionMapper.From(edit.Zone, edit.Settings), cancellationToken);
        if (!published.IsSuccess)
        {
            return Result.Fail(published.Error!);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await edit.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ZoneChangedMeanwhile), ErrorType.Conflict));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The natural-key index refused a record the domain's own duplicate check did not see, which is
            // what two edits that raced each other past the lock would look like. Any other database
            // failure is not a duplicate and is left to propagate as the fault it is.
            return Result.Fail(Error.Of(nameof(ErrorMessages.RecordDuplicate), ErrorType.Conflict));
        }

        return Result.Ok();
    }

    /// <summary>Tells whether a failed save was PostgreSQL refusing a unique index.</summary>
    /// <param name="exception">The save failure.</param>
    /// <returns>True for SQLSTATE 23505.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
