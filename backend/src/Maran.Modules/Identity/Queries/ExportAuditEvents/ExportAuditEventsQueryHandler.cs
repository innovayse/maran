using System.Diagnostics;
using System.Globalization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Queries.ExportAuditEvents;

/// <summary>
/// Handles <see cref="ExportAuditEventsQuery"/> by deciding whether the export may happen, journalling
/// that it will, and answering with the plan the writer streams from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order matters.</b> The handler counts the matching rows, refuses a file above
/// <see cref="AuditExportLimits.MaxRows"/> (a 400 saying to narrow the window, never a truncation), and
/// only then writes the <c>AuditExported</c> entry and returns. The entry therefore exists before a byte
/// is streamed: an export is authorised and recorded even if the client disconnects, and a refused one is
/// not recorded as taken. It carries the planned row count, not the delivered one.
/// </para>
/// <para>
/// The handler reads no row. The rows are read by <see cref="AuditExportWriter"/> in batches, bounded by
/// the count taken here, so a file never sits in memory and entries written while it streams cannot grow it.
/// </para>
/// </remarks>
public sealed class ExportAuditEventsQueryHandler
{
    /// <summary>The format of the instant in a file name: sortable, and free of characters a file system dislikes.</summary>
    private const string FileNameInstantFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The audit journal, narrowed by a filter.</summary>
    private readonly AuditEventQuery _events;

    /// <summary>Records the export.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock, which names the file.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="events">The audit journal, narrowed by a filter.</param>
    /// <param name="journal">Records the export.</param>
    /// <param name="clock">The panel's clock.</param>
    public ExportAuditEventsQueryHandler(AuditEventQuery events, IdentityAuditJournal journal, IClock clock)
    {
        _events = events;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Counts, checks the cap, journals the export and returns its plan.</summary>
    /// <param name="query">The validated export request and the administrator asking.</param>
    /// <param name="cancellationToken">Cancels the count and the journal write.</param>
    /// <returns>
    /// The plan, or <c>AuditExportTooLarge</c> when more than <see cref="AuditExportLimits.MaxRows"/> rows match.
    /// </returns>
    public async Task<Result<AuditExportPlan>> HandleAsync(
        ExportAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        var filter = new AuditEventFilter(
            query.Action,
            query.Outcome,
            query.From,
            query.To,
            query.Search,
            query.Actor,
            query.Account);

        var rowCount = await _events.Matching(filter).LongCountAsync(cancellationToken);

        if (rowCount > AuditExportLimits.MaxRows)
        {
            return Result<AuditExportPlan>.Fail(
                Error.Of(nameof(ErrorMessages.AuditExportTooLarge), ErrorType.Validation));
        }

        await _journal.RecordIdentifiedAsync(
            query.RequestedByUserId,
            IdentityAuditActions.AuditExported,
            AuditExportSubjectText.Of(query.Format, filter, rowCount),
            query.IpAddress,
            query.UserAgent,
            succeeded: true,
            cancellationToken);

        var (extension, contentType) = FileKindOf(query.Format);
        var fileName = "maran-audit-"
            + _clock.UtcNow.UtcDateTime.ToString(FileNameInstantFormat, CultureInfo.InvariantCulture)
            + "." + extension;

        return Result<AuditExportPlan>.Ok(new AuditExportPlan(query.Format, filter, rowCount, fileName, contentType));
    }

    /// <summary>The file extension and media type of a format.</summary>
    /// <param name="format">The file format.</param>
    /// <returns>The extension without its dot, and the media type.</returns>
    private static (string Extension, string ContentType) FileKindOf(AuditExportFormat format)
    {
        switch (format)
        {
            case AuditExportFormat.Csv:
                return ("csv", "text/csv; charset=utf-8");
            case AuditExportFormat.Json:
                return ("json", "application/json");
            default:
                throw new UnreachableException("The validator admits only the two formats the enum names.");
        }
    }
}
