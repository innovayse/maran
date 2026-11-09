using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.DeleteZone;

/// <summary>
/// Handles <see cref="DeleteZoneCommand"/>: takes the zone off the name server, and then removes the row
/// that owned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which zone is removed is decided by the panel's row and by nothing else, and the refusal for somebody
/// else's row is indistinguishable from the refusal for a row that does not exist.</b> The row is loaded
/// through the tenant-filtered context, so another account's identifier finds nothing; the single
/// <c>ZoneNotFound</c> answer covers both. The agent is addressed by the name stored in the caller's own
/// row, never by a name the caller supplied.
/// </para>
/// <para>
/// The agent runs first, as everywhere in this module: a zone removed from the host with its row still
/// present is visible, retryable and converges, because the agent reports a zone it does not serve as
/// not found and that is exactly what this handler accepts. A row removed with the zone still served is a
/// zone answering the internet for a name nobody in the panel can see or stop.
/// </para>
/// <para>
/// It takes the zone's lock, so an edit that is publishing the zone cannot put it back on the host after
/// this handler removed it.
/// </para>
/// </remarks>
public sealed class DeleteZoneCommandHandler
{
    /// <summary>The agent's code for a zone it does not serve, which deleting treats as already done.</summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Serialises everything that changes one zone.</summary>
    private readonly IDnsZoneLock _lock;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="agent">The agent client that removes the zone.</param>
    /// <param name="zoneLock">Serialises everything that changes one zone.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteZoneCommandHandler(
        DnsDbContext dbContext,
        IAgentDnsClient agent,
        IDnsZoneLock zoneLock,
        DnsAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _lock = zoneLock;
        _journal = journal;
    }

    /// <summary>Removes the zone. Not idempotent: a second attempt finds no row and answers not found.</summary>
    /// <param name="command">Which zone to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <c>ZoneNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result> HandleAsync(DeleteZoneCommand command, CancellationToken cancellationToken)
    {
        var visible = await _dbContext.Zones
            .AsNoTracking()
            .AnyAsync(row => row.Id == command.ZoneId, cancellationToken);
        if (!visible)
        {
            // The subject is the identifier the caller supplied, because no name is known: a probe for a zone
            // the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.ZoneId.ToString(),
                Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        await using var transaction = await _lock.TakeAsync(command.ZoneId, cancellationToken);
        var zone = await _dbContext.Zones.SingleOrDefaultAsync(row => row.Id == command.ZoneId, cancellationToken);
        if (zone is null)
        {
            return await FailAsync(
                command,
                command.ZoneId.ToString(),
                Error.Of(nameof(ErrorMessages.ZoneNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var name = zone.Name;
        var removed = await _agent.DeleteZoneAsync(name, cancellationToken);
        if (!removed.IsSuccess && !string.Equals(removed.Error!.Code, AgentNotFoundCode, StringComparison.Ordinal))
        {
            return await FailAsync(command, name, removed.Error!, cancellationToken);
        }

        _dbContext.Zones.Remove(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsZoneDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused delete and returns it as the typed failure.</summary>
    /// <param name="command">The delete that was refused.</param>
    /// <param name="subject">The zone's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteZoneCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsZoneDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
