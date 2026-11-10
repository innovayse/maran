using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.EnableZoneSigning;

/// <summary>
/// Handles <see cref="EnableZoneSigningCommand"/>: opens an edit of the zone, makes sure this host can sign,
/// generates the key through the zone's own method and publishes the zone once.
/// </summary>
/// <remarks>
/// <para>
/// The host is asked first and the zone is changed second, so a refused request leaves nothing to undo. The
/// answer is the agent's observation of the daemon (<see cref="DnssecHostProbe"/>), never a version table.
/// </para>
/// <para>
/// The key is generated here, from the platform's own cryptography, and reaches the database encrypted and
/// the agent over the local socket. The audit entry names the zone and never the key.
/// </para>
/// </remarks>
public sealed class EnableZoneSigningCommandHandler
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
    /// <param name="probe">Asks whether this host can sign.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public EnableZoneSigningCommandHandler(
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

    /// <summary>Switches signing on.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The zone's DNSSEC situation, including the DS record to publish; <c>ZoneNotFound</c>;
    /// <c>ZoneSigningHostCannotSign</c>; <c>ZoneSigningNotOff</c>; or the agent's typed failure.
    /// </returns>
    public async Task<Result<ZoneDnssecDto>> HandleAsync(
        EnableZoneSigningCommand command,
        CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenAsync(command.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return await FailAsync(command, command.ZoneId.ToString(), opened.Error!, cancellationToken);
        }

        await using var edit = opened.Value;
        var zone = edit.Zone;

        var supported = await _probe.IsSupportedAsync(cancellationToken);
        if (!supported.IsSuccess)
        {
            return await FailAsync(command, zone.Name, supported.Error!, cancellationToken);
        }

        if (!supported.Value)
        {
            return await FailAsync(
                command,
                zone.Name,
                Error.Of(nameof(ErrorMessages.ZoneSigningHostCannotSign), ErrorType.Conflict),
                cancellationToken);
        }

        var verdict = zone.EnableSigning(Guid.NewGuid(), DnssecKeyPolicy.Generate(), _clock.UtcNow);
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
            DnsAuditActions.ZoneSigningEnabled, zone.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneDnssecDto>.Ok(ZoneDnssecMapper.From(zone, available: true, observed: null));
    }

    /// <summary>Journals a refused request and returns it as the typed failure.</summary>
    /// <param name="command">The request that was refused.</param>
    /// <param name="subject">The zone's name, or the supplied identifier when no zone was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ZoneDnssecDto>> FailAsync(
        EnableZoneSigningCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.ZoneSigningEnabled, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneDnssecDto>.Fail(error);
    }
}
