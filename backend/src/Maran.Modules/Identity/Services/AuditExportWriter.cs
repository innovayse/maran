using System.Runtime.CompilerServices;
using System.Text;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Streams an authorised export of the audit journal into an HTTP response: sets the download headers,
/// then reads the matching rows in chronological order, a batch at a time, and writes them as CSV or JSON.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is bounded by the plan, not merely checked against the cap.</b> The handler counted the rows and
/// journalled that count; this writer stops after exactly that many, so entries written while a long
/// download runs cannot push the file past what was authorised and recorded. Rows removed meanwhile (a
/// retention run) make it shorter, never longer.
/// </para>
/// <para>
/// <b>Paging is by keyset, not by offset.</b> Each batch asks for the rows after the last instant already
/// written, ordered by (<c>OccurredAt</c>, <c>Id</c>), so the cost of a batch does not grow with how far
/// into the file it is. Entries that share one instant are told apart by the ids already written at it,
/// rather than by comparing ids here: the database orders uuids its own way, and a comparison made in
/// .NET could disagree with that order and skip or repeat a row.
/// </para>
/// <para>
/// <b>Headers.</b> <c>Content-Disposition: attachment</c> with the plan's file name (built from the clock
/// and the format, never from caller text) and <c>Cache-Control: no-store</c>. Memory holds one batch.
/// </para>
/// </remarks>
public sealed class AuditExportWriter
{
    /// <summary>UTF-8 without a byte-order mark, which some spreadsheets show as stray characters in the first header.</summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The audit journal, narrowed by a filter.</summary>
    private readonly AuditEventQuery _events;

    /// <summary>Creates the writer.</summary>
    /// <param name="events">The audit journal, narrowed by a filter.</param>
    public AuditExportWriter(AuditEventQuery events)
    {
        _events = events;
    }

    /// <summary>Writes the export as the body of <paramref name="response"/>.</summary>
    /// <param name="response">The response to write to; its headers must not have been sent.</param>
    /// <param name="plan">The authorised, journalled plan to write.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects; stops the reads with it.</param>
    /// <returns>Resolves once the whole file is written and flushed.</returns>
    public async Task WriteAsync(HttpResponse response, AuditExportPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(plan);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = plan.ContentType;
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = $"attachment; filename=\"{plan.FileName}\"";
        await response.StartAsync(cancellationToken);

        var batches = ReadBatchesAsync(plan, cancellationToken);

        if (plan.Format == AuditExportFormat.Csv)
        {
            await WriteCsvAsync(response.Body, batches, cancellationToken);
        }
        else
        {
            await AuditExportJsonWriter.WriteAsync(response.Body, batches, cancellationToken);
        }
    }

    /// <summary>Writes the header record, then each batch's records, flushing after every batch.</summary>
    /// <param name="body">The stream the file is written to; left open.</param>
    /// <param name="batches">The rows, in groups.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    private static async Task WriteCsvAsync(
        Stream body,
        IAsyncEnumerable<IReadOnlyList<AuditExportRow>> batches,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(body, Utf8WithoutBom, leaveOpen: true);

        await writer.WriteAsync(AuditExportCsvMapper.Header().AsMemory(), cancellationToken);

        await foreach (var batch in batches.WithCancellation(cancellationToken))
        {
            foreach (var row in batch)
            {
                await writer.WriteAsync(AuditExportCsvMapper.Line(row).AsMemory(), cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the plan's rows oldest first, <see cref="AuditExportLimits.BatchSize"/> at a time, and
    /// stops after <see cref="AuditExportPlan.RowCount"/> of them.
    /// </summary>
    /// <param name="plan">The plan whose filter and row count bound the read.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The rows as export rows, grouped by the batch they were read in.</returns>
    private async IAsyncEnumerable<IReadOnlyList<AuditExportRow>> ReadBatchesAsync(
        AuditExportPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remaining = Math.Min(plan.RowCount, AuditExportLimits.MaxRows);
        DateTimeOffset? lastInstant = null;
        var writtenAtLastInstant = new List<Guid>();

        while (remaining > 0)
        {
            var take = (int)Math.Min(AuditExportLimits.BatchSize, remaining);
            var events = _events.Matching(plan.Filter);

            if (lastInstant is { } instant)
            {
                // A snapshot, so the query's parameter is the ids written so far and not a list that
                // the loop below goes on changing.
                var written = writtenAtLastInstant.ToArray();
                events = events.Where(auditEvent =>
                    auditEvent.OccurredAt > instant
                    || (auditEvent.OccurredAt == instant && !written.Contains(auditEvent.Id)));
            }

            var page = await events
                .OrderBy(auditEvent => auditEvent.OccurredAt)
                .ThenBy(auditEvent => auditEvent.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                yield break;
            }

            lastInstant = RememberPage(page, lastInstant, writtenAtLastInstant);
            remaining -= page.Count;

            yield return page.Select(AuditExportRowMapper.From).ToList();
        }
    }

    /// <summary>
    /// Moves the keyset forward past a page: the last instant read, and the ids already written at it.
    /// </summary>
    /// <param name="page">The page just read, oldest first and not empty.</param>
    /// <param name="lastInstant">The last instant read before this page, or <c>null</c> before the first.</param>
    /// <param name="writtenAtLastInstant">
    /// The ids written at the last instant; kept while the instant continues across pages, replaced when
    /// the page moves to a later one.
    /// </param>
    /// <returns>The page's last instant, which is the new keyset.</returns>
    private static DateTimeOffset RememberPage(
        List<AuditEvent> page,
        DateTimeOffset? lastInstant,
        List<Guid> writtenAtLastInstant)
    {
        var pageLast = page[^1].OccurredAt;

        if (lastInstant != pageLast)
        {
            writtenAtLastInstant.Clear();
        }

        foreach (var auditEvent in page)
        {
            if (auditEvent.OccurredAt == pageLast)
            {
                writtenAtLastInstant.Add(auditEvent.Id);
            }
        }

        return pageLast;
    }
}
