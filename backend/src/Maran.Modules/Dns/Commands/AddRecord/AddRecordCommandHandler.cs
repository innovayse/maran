using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Commands.AddRecord;

/// <summary>
/// Handles <see cref="AddRecordCommand"/>: opens an edit of the zone, adds the record through the zone's own
/// rules, and publishes the whole zone.
/// </summary>
/// <remarks>
/// The shape of the record is judged once more here against the zone's real name, because the boundary
/// validator could only judge an owner name against an empty apex. Every refusal, including a zone another
/// account owns, is journalled before it is answered.
/// </remarks>
public sealed class AddRecordCommandHandler
{
    /// <summary>Opens and publishes edits of one zone.</summary>
    private readonly DnsZoneEditor _editor;

    /// <summary>The operator's settings: how many records a zone may hold.</summary>
    private readonly IOptions<DnsOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant and the serial are taken from.</summary>
    private readonly IClock _clock;

    /// <summary>The viewer's-language lookup of the module's display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="editor">Opens and publishes edits of one zone.</param>
    /// <param name="options">The operator's settings.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    public AddRecordCommandHandler(
        DnsZoneEditor editor,
        IOptions<DnsOptions> options,
        DnsAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names)
    {
        _editor = editor;
        _options = options;
        _journal = journal;
        _clock = clock;
        _names = names;
    }

    /// <summary>Adds the record.</summary>
    /// <param name="command">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The added record, or the typed reason it was refused.</returns>
    public async Task<Result<ZoneRecordDto>> HandleAsync(
        AddRecordCommand command,
        CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenAsync(command.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return await FailAsync(command, command.ZoneId.ToString(), opened.Error!, cancellationToken);
        }

        await using var edit = opened.Value;
        var zone = edit.Zone;
        var draft = RecordShapePolicy.Normalize(command.Record);
        var subject = $"{zone.Name}: {draft.Name} {draft.Type}";

        var shape = RecordShapePolicy.Check(draft, zone.Name);
        if (shape != RecordShapeVerdict.Accepted)
        {
            return await FailAsync(
                command, subject, Error.Of(RecordShapeMessages.KeyOf(shape), ErrorType.Validation), cancellationToken);
        }

        var recordId = Guid.NewGuid();
        var verdict = zone.AddRecord(recordId, draft, _options.Value.MaxRecordsPerZone, _clock.UtcNow);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return await FailAsync(command, subject, RecordRuleErrors.From(verdict), cancellationToken);
        }

        var published = await _editor.PublishAsync(edit, cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, subject, published.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsRecordAdded, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneRecordDto>.Ok(ZoneRecordMapper.From(zone.Records.Single(record =>
        {
            return record.Id == recordId;
        }), _names));
    }

    /// <summary>Journals a refused addition and returns it as the typed failure.</summary>
    /// <param name="command">The addition that was refused.</param>
    /// <param name="subject">The zone and record, or the supplied identifier when no zone was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ZoneRecordDto>> FailAsync(
        AddRecordCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsRecordAdded, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneRecordDto>.Fail(error);
    }
}
