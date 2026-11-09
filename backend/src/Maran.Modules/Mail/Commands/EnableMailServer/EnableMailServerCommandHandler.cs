using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Mail.Commands.EnableMailServer;

/// <summary>
/// Handles <see cref="EnableMailServerCommand"/>: asks the agent to configure and start the mail
/// daemons, then records what the administrator asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent runs before the row is written.</b> Daemons running with no settings row show on the
/// status screen, which reads the agent, and enabling again converges. A row saying mail is on over
/// daemons that never started tells an operator their customers can send when they cannot.
/// </para>
/// <para>
/// Enabling an enabled server is not refused: the agent converges to the settings given, which is
/// also how an operator repairs a host whose configuration was lost. The webmail master hash is not
/// part of this call, so none is sent.
/// </para>
/// </remarks>
public sealed class EnableMailServerCommandHandler
{
    /// <summary>The Mail module's database context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The agent, which owns the daemons and everything else that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injectable time source; the ambient clock is a banned symbol.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="agent">The agent client that configures and starts the daemons.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injectable time source.</param>
    public EnableMailServerCommandHandler(
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

    /// <summary>Enables the mail server and answers with what the host does afterwards.</summary>
    /// <param name="command">The validated settings.</param>
    /// <param name="cancellationToken">Cancels the agent call and the writes.</param>
    /// <returns>The status after applying the settings, or the agent's typed failure.</returns>
    public async Task<Result<MailServerStatusDto>> HandleAsync(
        EnableMailServerCommand command,
        CancellationToken cancellationToken)
    {
        var hostname = command.Hostname.ToLowerInvariant();

        var enabled = await _agent.EnableAsync(
            AgentMailServerSettingsMapper.ToAgent(command, hostname),
            cancellationToken);

        if (!enabled.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                HostedMailAuditActions.MailServerEnabled,
                hostname,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<MailServerStatusDto>.Fail(enabled.Error!);
        }

        var settings = await _dbContext.MailServerSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new MailServerSettings(
                hostname,
                command.Pop3Enabled,
                command.AntivirusEnabled,
                command.AllowExternalForwarding,
                command.MaxMessageMib,
                command.SpamGreylistScore,
                command.SpamAddHeaderScore,
                command.SpamRejectScore,
                _clock.UtcNow);

            await _dbContext.MailServerSettings.AddAsync(settings, cancellationToken);
        }
        else
        {
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
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailServerEnabled,
            hostname,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailServerStatusDto>.Ok(MailServerStatusMapper.ToDto(enabled.Value, settings));
    }
}
