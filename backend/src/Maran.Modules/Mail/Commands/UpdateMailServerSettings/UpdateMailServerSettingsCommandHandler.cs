using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Mail.Commands.UpdateMailServerSettings;

/// <summary>
/// Handles <see cref="UpdateMailServerSettingsCommand"/>: re-states the settings of a server that is
/// on, through the agent's enable call, which converges the host to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A server that is off is refused with <c>MailNotEnabled</c> before the agent is asked.</b> The
/// agent's enable call is the only one that configures the daemons, so without this check an update
/// would switch mail on and the row, written next, would say so: an administrator tuning a number on
/// a stopped server must not start four daemons.
/// </para>
/// <para>
/// The agent runs before the row is written, for the reason it does on the enable path: the row is
/// an intention, and an intention recorded over a configuration the daemons refused is a panel
/// reporting settings the host does not have.
/// </para>
/// </remarks>
public sealed class UpdateMailServerSettingsCommandHandler
{
    /// <summary>The Mail module's database context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The agent, which owns the daemons.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injectable time source; the ambient clock is a banned symbol.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="agent">The agent client that reconfigures the daemons.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injectable time source.</param>
    public UpdateMailServerSettingsCommandHandler(
        MailDbContext dbContext,
        IAgentMailClient agent,
        MailAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Re-states the settings and answers with what the host does afterwards.</summary>
    /// <param name="command">The validated settings.</param>
    /// <param name="cancellationToken">Cancels the reads, the agent call and the writes.</param>
    /// <returns>The status after applying the settings, <c>MailNotEnabled</c>, or the agent's typed failure.</returns>
    public async Task<Result<MailServerStatusDto>> HandleAsync(
        UpdateMailServerSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var hostname = command.Hostname.ToLowerInvariant();

        var settings = await _dbContext.MailServerSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is not { Enabled: true })
        {
            return await FailAsync(
                command,
                hostname,
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict),
                cancellationToken);
        }

        var applied = await _agent.EnableAsync(
            AgentMailServerSettingsMapper.ToAgent(command, hostname),
            cancellationToken);

        if (!applied.IsSuccess)
        {
            return await FailAsync(command, hostname, applied.Error!, cancellationToken);
        }

        settings.Enable(
            hostname,
            command.Pop3Enabled,
            command.AntivirusEnabled,
            command.AllowExternalForwarding,
            command.MaxMessageMib,
            command.SpamGreylistScore,
            command.SpamAddHeaderScore,
            command.SpamRejectScore,
            _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailServerSettingsUpdated,
            hostname,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailServerStatusDto>.Ok(MailServerStatusMapper.ToDto(applied.Value, settings));
    }

    /// <summary>Journals a refused update and returns it as the typed failure.</summary>
    /// <param name="command">The update that was refused.</param>
    /// <param name="hostname">The host name the update named, as the journal's subject.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailServerStatusDto>> FailAsync(
        UpdateMailServerSettingsCommand command,
        string hostname,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailServerSettingsUpdated,
            hostname,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailServerStatusDto>.Fail(error);
    }
}
