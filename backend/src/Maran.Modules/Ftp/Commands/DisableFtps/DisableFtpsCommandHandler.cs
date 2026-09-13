using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Mappers;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Services;

namespace Maran.Modules.Ftp.Commands.DisableFtps;

/// <summary>
/// Handles <see cref="DisableFtpsCommand"/>: stops the daemon on the host, then records that the
/// panel no longer wants it running.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no "not enabled" refusal, and that is deliberate.</b> The agent's disable is
/// idempotent, so a server whose daemon is already down converges rather than fails — and a panel
/// that refused would leave an operator unable to switch off a daemon the panel had lost track of,
/// which is precisely the state they most want to be able to end.
/// </para>
/// <para>
/// The agent runs first, as on the enable path and for the mirror-image reason. A stopped daemon
/// with the row still saying "enabled" is a disagreement the status screen shows; a row saying
/// "disabled" over a daemon still listening is a panel reporting a closed door that is open.
/// </para>
/// <para>
/// The hostname and the range survive on the row: an operator who re-enables expects the same
/// hostname and the same ports, and retyping them is how the panel and the firewall stop agreeing.
/// </para>
/// </remarks>
public sealed class DisableFtpsCommandHandler
{
    /// <summary>The Ftp module's database context.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The agent, which owns the daemon and is the only thing that may stop it.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FtpAuditJournal _journal;

    /// <summary>The injectable time source; the ambient clock is a banned symbol.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="agent">The agent client that stops the daemon.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injectable time source.</param>
    public DisableFtpsCommandHandler(
        FtpDbContext dbContext,
        IAgentFtpsClient agent,
        FtpAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Stops the daemon and answers with what the agent observed afterwards.</summary>
    /// <param name="command">The caller's address and client, for the journal.</param>
    /// <param name="cancellationToken">Cancels the agent call and the writes.</param>
    /// <returns>The daemon's state after stopping, or the agent's own typed failure.</returns>
    public async Task<Result<FtpsStatusDto>> HandleAsync(
        DisableFtpsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.FtpsSettings.SingleOrDefaultAsync(cancellationToken);

        // The subject of the entry when nothing was ever enabled: there is no hostname to name, and
        // an empty subject is honest where a fabricated one would be searchable and wrong.
        var subject = settings?.Hostname ?? string.Empty;

        var disabled = await _agent.DisableAsync(cancellationToken);
        if (!disabled.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                FtpAuditJournal.FtpsDisabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<FtpsStatusDto>.Fail(disabled.Error!);
        }

        if (settings is not null)
        {
            settings.Disable(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            FtpAuditJournal.FtpsDisabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<FtpsStatusDto>.Ok(FtpsStatusMapper.ToDto(disabled.Value, settings));
    }
}
