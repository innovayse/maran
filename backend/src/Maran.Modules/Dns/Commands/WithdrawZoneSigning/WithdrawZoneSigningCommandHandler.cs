using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.WithdrawZoneSigning;

/// <summary>
/// Handles <see cref="WithdrawZoneSigningCommand"/>: opens an edit of the zone, begins the withdrawal through
/// the zone's own method and publishes the zone once, with the SAME key under the insecure policy.
/// </summary>
/// <remarks>
/// The key is kept on purpose. Resolvers that cached the registrar's DS record keep getting answers they can
/// validate while the record is removed, which is what makes the later step safe to take.
/// </remarks>
public sealed class WithdrawZoneSigningCommandHandler
{
    /// <summary>Opens and publishes edits of one zone.</summary>
    private readonly DnsZoneEditor _editor;

    /// <summary>Asks whether this host's name server software can sign.</summary>
    private readonly DnssecHostProbe _probe;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant and the serial are taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="editor">Opens and publishes edits of one zone.</param>
    /// <param name="probe">Asks whether this host can sign, for the answer's availability flag.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public WithdrawZoneSigningCommandHandler(
        DnsZoneEditor editor,
        DnssecHostProbe probe,
        DnsAuditJournal journal,
        IClock clock)
    {
        _editor = editor;
        _probe = probe;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Begins the withdrawal.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The zone's DNSSEC situation, including when the withdrawal may end; <c>ZoneNotFound</c>;
    /// <c>ZoneSigningNotSigning</c>; or the agent's typed failure.
    /// </returns>
    public async Task<Result<ZoneDnssecDto>> HandleAsync(
        WithdrawZoneSigningCommand command,
        CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenAsync(command.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return await FailAsync(command, command.ZoneId.ToString(), opened.Error!, cancellationToken);
        }

        await using var edit = opened.Value;
        var zone = edit.Zone;

        var verdict = zone.BeginWithdrawal(_clock.UtcNow);
        if (verdict != DnssecTransitionVerdict.Accepted)
        {
            return await FailAsync(command, zone.Name, DnssecTransitionErrors.From(verdict), cancellationToken);
        }

        var published = await _editor.PublishAsync(edit, cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, zone.Name, published.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.ZoneSigningWithdrawalStarted,
            zone.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var available = await _probe.IsAvailableAsync(cancellationToken);

        return Result<ZoneDnssecDto>.Ok(ZoneDnssecMapper.From(zone, available, observed: null));
    }

    /// <summary>Journals a refused request and returns it as the typed failure.</summary>
    /// <param name="command">The request that was refused.</param>
    /// <param name="subject">The zone's name, or the supplied identifier when no zone was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ZoneDnssecDto>> FailAsync(
        WithdrawZoneSigningCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.ZoneSigningWithdrawalStarted,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<ZoneDnssecDto>.Fail(error);
    }
}
