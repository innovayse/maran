using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Commands.ReplaceRecords;

/// <summary>
/// Handles <see cref="ReplaceRecordsCommand"/>: opens an edit of the zone, swaps the customer's record set
/// in through the zone's own rules, and publishes the whole zone once.
/// </summary>
/// <remarks>
/// All or nothing: the first record the zone refuses refuses the whole import and the zone is untouched,
/// because a half-applied import is a zone nobody wrote.
/// </remarks>
public sealed class ReplaceRecordsCommandHandler
{
    /// <summary>Opens and publishes edits of one zone.</summary>
    private readonly DnsZoneEditor _editor;

    /// <summary>The operator's settings: how many records a zone may hold.</summary>
    private readonly IOptions<DnsOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant and the serial are taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="editor">Opens and publishes edits of one zone.</param>
    /// <param name="options">The operator's settings.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public ReplaceRecordsCommandHandler(
        DnsZoneEditor editor,
        IOptions<DnsOptions> options,
        DnsAuditJournal journal,
        IClock clock)
    {
        _editor = editor;
        _options = options;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Replaces the customer's record set.</summary>
    /// <param name="command">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the typed reason the import was refused.</returns>
    public async Task<Result> HandleAsync(ReplaceRecordsCommand command, CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenAsync(command.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return await FailAsync(command, command.ZoneId.ToString(), opened.Error!, cancellationToken);
        }

        await using var edit = opened.Value;
        var zone = edit.Zone;

        var drafts = command.Records.Select(RecordShapePolicy.Normalize).ToList();
        foreach (var draft in drafts)
        {
            var shape = RecordShapePolicy.Check(draft, zone.Name);
            if (shape != RecordShapeVerdict.Accepted)
            {
                return await FailAsync(
                    command, zone.Name, Error.Of(RecordShapeMessages.KeyOf(shape), ErrorType.Validation), cancellationToken);
            }
        }

        var verdict = zone.ReplaceRecords(drafts, _options.Value.MaxRecordsPerZone, _clock.UtcNow);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return await FailAsync(command, zone.Name, RecordRuleErrors.From(verdict), cancellationToken);
        }

        var published = await _editor.PublishAsync(edit, cancellationToken);
        if (!published.IsSuccess)
        {
            return await FailAsync(command, zone.Name, published.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsRecordsReplaced, zone.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused import and returns it as the typed failure.</summary>
    /// <param name="command">The import that was refused.</param>
    /// <param name="subject">The zone's name, or the supplied identifier when no zone was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        ReplaceRecordsCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsRecordsReplaced, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
