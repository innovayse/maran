using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.DisableDns;

/// <summary>
/// Handles <see cref="DisableDnsCommand"/>: asks the agent to stop the name server, and only then records
/// that the administrator wanted it off.
/// </summary>
/// <remarks>
/// A server that never stored settings has nothing to record and still gets the agent's stop, which is
/// idempotent: the daemon may have been started by an earlier release or by hand, and "off" must mean off.
/// </remarks>
public sealed class DisableDnsCommandHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns the daemon.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant is taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="agent">The agent client that stops the daemon.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public DisableDnsCommandHandler(
        DnsDbContext dbContext,
        IAgentDnsClient agent,
        DnsAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Switches the name server off.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status the agent observed, or the agent's own typed failure.</returns>
    public async Task<Result<DnsStatusDto>> HandleAsync(DisableDnsCommand command, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        var subject = settings?.PrimaryNameserver ?? string.Empty;

        var disabled = await _agent.DisableAsync(cancellationToken);
        if (!disabled.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                DnsAuditActions.DnsDisabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<DnsStatusDto>.Fail(disabled.Error!);
        }

        if (settings is not null)
        {
            settings.Disable(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsDisabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<DnsStatusDto>.Ok(DnsStatusMapper.Observed(enabled: false, disabled.Value, zoneCount: 0));
    }
}
