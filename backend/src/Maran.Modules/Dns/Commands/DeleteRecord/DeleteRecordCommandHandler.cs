using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.DeleteRecord;

/// <summary>
/// Handles <see cref="DeleteRecordCommand"/>: opens an edit of the zone, removes the record through the
/// zone's own rules, and publishes the whole zone.
/// </summary>
public sealed class DeleteRecordCommandHandler
{
    /// <summary>Opens and publishes edits of one zone.</summary>
    private readonly DnsZoneEditor _editor;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant and the serial are taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="editor">Opens and publishes edits of one zone.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public DeleteRecordCommandHandler(DnsZoneEditor editor, DnsAuditJournal journal, IClock clock)
    {
        _editor = editor;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Removes the record.</summary>
    /// <param name="command">Which record to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the typed reason the removal was refused.</returns>
    public async Task<Result> HandleAsync(DeleteRecordCommand command, CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenAsync(command.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return await FailAsync(command, command.ZoneId.ToString(), opened.Error!, cancellationToken);
        }

        await using var edit = opened.Value;
        var zone = edit.Zone;

        // Named before it goes: the journal records the name and type, which is all anyone will later be able
        // to search for. A record this zone does not hold is journalled under the identifier supplied.
        var existing = zone.Records.SingleOrDefault(record =>
        {
            return record.Id == command.RecordId;
        });
        var subject = existing is null
            ? $"{zone.Name}: {command.RecordId}"
            : $"{zone.Name}: {existing.Name} {existing.Type}";

        var verdict = zone.RemoveRecord(command.RecordId, _clock.UtcNow);
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
            DnsAuditActions.DnsRecordDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused removal and returns it as the typed failure.</summary>
    /// <param name="command">The removal that was refused.</param>
    /// <param name="subject">The zone and record, or the supplied identifier when no zone was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteRecordCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsRecordDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
