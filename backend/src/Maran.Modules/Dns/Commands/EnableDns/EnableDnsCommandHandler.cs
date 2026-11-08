using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.EnableDns;

/// <summary>
/// Handles <see cref="EnableDnsCommand"/>: asks the agent to start the name server, and only then records
/// that the administrator wanted it on.
/// </summary>
/// <remarks>
/// The agent runs first, as everywhere in this module: a refused start leaves the stored intention off, so
/// the screen never says "on" about a daemon that did not come up. The answer carries what the agent
/// observed, not what was asked. This is the one operation that opens a port to the internet, so it is
/// administrator-only, rate-limited and journalled on refusal as well as on success.
/// </remarks>
public sealed class EnableDnsCommandHandler
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
    /// <param name="agent">The agent client that starts the daemon.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public EnableDnsCommandHandler(
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

    /// <summary>Switches the name server on.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status the agent observed, <c>DnsSettingsIncomplete</c>, or the agent's own typed failure.</returns>
    public async Task<Result<DnsStatusDto>> HandleAsync(EnableDnsCommand command, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings is null || !settings.IsComplete)
        {
            return await FailAsync(
                command,
                string.Empty,
                Error.Of(nameof(ErrorMessages.DnsSettingsIncomplete), ErrorType.Conflict),
                cancellationToken);
        }

        var enabled = await _agent.EnableAsync(cancellationToken);
        if (!enabled.IsSuccess)
        {
            return await FailAsync(command, settings.PrimaryNameserver, enabled.Error!, cancellationToken);
        }

        settings.Enable(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsEnabled,
            settings.PrimaryNameserver,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DnsStatusDto>.Ok(DnsStatusMapper.Observed(enabled: true, enabled.Value, zoneCount: 0));
    }

    /// <summary>Journals a refused start and returns it as the typed failure.</summary>
    /// <param name="command">The request that was refused.</param>
    /// <param name="subject">The primary name server, or empty when none is stated.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<DnsStatusDto>> FailAsync(
        EnableDnsCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsEnabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<DnsStatusDto>.Fail(error);
    }
}
