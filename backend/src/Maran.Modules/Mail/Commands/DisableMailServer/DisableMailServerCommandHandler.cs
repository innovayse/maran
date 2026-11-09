using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Mail.Commands.DisableMailServer;

/// <summary>
/// Handles <see cref="DisableMailServerCommand"/>: stops the daemons on the host, then records that
/// the panel no longer wants them running.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no "not enabled" refusal.</b> The agent's disable is idempotent, and a panel that
/// refused would leave an operator unable to switch off daemons the panel had lost track of, which is
/// the state they most want to end. With no row the answer carries no settings, so a server nobody
/// configured is not reported as one an administrator turned off.
/// </para>
/// <para>
/// The agent runs first: a stopped stack under a row saying "on" is a disagreement the status screen
/// shows, while a row saying "off" over daemons still listening reports a closed door that is open.
/// The hostname and every setting survive on the row, so a later enable starts from them.
/// </para>
/// </remarks>
public sealed class DisableMailServerCommandHandler
{
    /// <summary>The Mail module's database context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The agent, which owns the daemons and is the only thing that may stop them.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injectable time source; the ambient clock is a banned symbol.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="agent">The agent client that stops the daemons.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injectable time source.</param>
    public DisableMailServerCommandHandler(
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

    /// <summary>Stops the daemons and answers with what the agent observed afterwards.</summary>
    /// <param name="command">The caller's address and client, for the journal.</param>
    /// <param name="cancellationToken">Cancels the agent call and the writes.</param>
    /// <returns>The status after stopping, or the agent's typed failure.</returns>
    public async Task<Result<MailServerStatusDto>> HandleAsync(
        DisableMailServerCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.MailServerSettings.SingleOrDefaultAsync(cancellationToken);

        // With nothing ever enabled there is no host name to name, and an empty subject is honest
        // where a made-up one would be searchable and wrong.
        var subject = settings?.Hostname ?? string.Empty;

        var disabled = await _agent.DisableAsync(cancellationToken);
        if (!disabled.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                HostedMailAuditActions.MailServerDisabled,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<MailServerStatusDto>.Fail(disabled.Error!);
        }

        if (settings is not null)
        {
            settings.Disable(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailServerDisabled,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailServerStatusDto>.Ok(MailServerStatusMapper.ToDto(disabled.Value, settings));
    }
}
