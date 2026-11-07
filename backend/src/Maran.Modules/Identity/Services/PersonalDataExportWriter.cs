using System.Globalization;
using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Sdk.Contracts;
using Microsoft.Net.Http.Headers;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Streams a <see cref="PersonalDataExportPlan"/> to the caller as one JSON document:
/// <c>{subject, generatedAt, sections, auditEntries}</c> (issue #146).
/// </summary>
/// <remarks>
/// <para>
/// <b>The stream is bounded by the plan, not merely checked against the cap.</b> The plan counted the
/// journal rows about the subject and the handler refused above <see cref="AuditExportLimits.MaxRows"/>;
/// this writer stops after exactly <see cref="PersonalDataExportPlan.AuditRowCount"/> rows, oldest first,
/// so entries written while the response is being produced cannot push an export past the cap that
/// was checked.
/// </para>
/// <para>
/// Rows are read in batches of <see cref="AuditExportLimits.BatchSize"/> ordered by (instant, id) and
/// the writer flushes after each batch. The batches are windows by offset into that ordering rather than
/// a keyset: the set is what ONE person or account did or had done to them, in the hundreds for a real
/// subject, and an offset needs no comparison of ids in the query. The cost is that a concurrent erasure
/// of the subject's own rows can move a window; the bound above still holds.
/// </para>
/// <para>
/// A journal row is written with the same projection as the journal export (<see cref="AuditExportRowMapper"/>)
/// minus the two chain columns, which describe the row's place in the tamper-evidence chain and say
/// nothing about the person. Headers carry <c>no-store</c> and an attachment name built by the handler
/// from the clock and the kind alone.
/// </para>
/// </remarks>
public sealed class PersonalDataExportWriter
{
    /// <summary>The chain columns of an export row that a personal-data export leaves out, by JSON name.</summary>
    private static readonly HashSet<string> OmittedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "chainIndex",
        "rowHash",
    };

    /// <summary>The naming the export row is serialised with: the web defaults, camel-case.</summary>
    private static readonly JsonSerializerOptions RowOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Chooses the journal rows about the subject.</summary>
    private readonly PersonalDataAuditRows _auditRows;

    /// <summary>The panel's clock, which stamps <c>generatedAt</c>.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the writer.</summary>
    /// <param name="auditRows">Chooses the journal rows about the subject.</param>
    /// <param name="clock">The panel's clock.</param>
    public PersonalDataExportWriter(PersonalDataAuditRows auditRows, IClock clock)
    {
        _auditRows = auditRows;
        _clock = clock;
    }

    /// <summary>Writes the headers and the document to <paramref name="response"/>.</summary>
    /// <param name="response">The response to stream into; nothing has been written to it yet.</param>
    /// <param name="plan">What to write, from <c>ExportPersonalDataQueryHandler</c>.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>Resolves once the whole document has been flushed.</returns>
    public async Task WriteAsync(
        HttpResponse response,
        PersonalDataExportPlan plan,
        CancellationToken cancellationToken)
    {
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = plan.FileName }.ToString();

        await using var json = new Utf8JsonWriter(response.Body);
        json.WriteStartObject();
        WriteSubject(json, plan.Subject);
        json.WriteString("generatedAt", _clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        WriteSections(json, plan.Sections);
        await WriteAuditEntriesAsync(json, plan, cancellationToken);
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
    }

    /// <summary>Writes the <c>subject</c> property.</summary>
    /// <param name="json">The document being written.</param>
    /// <param name="subject">The user or account the export is about.</param>
    private static void WriteSubject(Utf8JsonWriter json, PersonalDataSubject subject)
    {
        json.WriteStartObject("subject");
        json.WriteString("kind", subject.Kind.ToString().ToLowerInvariant());
        json.WriteString("id", subject.Id);
        json.WriteEndObject();
    }

    /// <summary>Writes the <c>sections</c> property: every provider's records as named string fields.</summary>
    /// <param name="json">The document being written.</param>
    /// <param name="sections">The sections the providers answered.</param>
    private static void WriteSections(Utf8JsonWriter json, IReadOnlyList<PersonalDataSection> sections)
    {
        json.WriteStartArray("sections");
        foreach (var section in sections)
        {
            json.WriteStartObject();
            json.WriteString("module", section.Module);
            json.WriteString("name", section.Name);
            json.WriteStartArray("records");
            foreach (var record in section.Records)
            {
                json.WriteStartObject();
                foreach (var field in record.Fields)
                {
                    json.WriteString(field.Key, field.Value);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// Writes the <c>auditEntries</c> property: the journal rows about the subject, oldest first, no more
    /// of them than the plan counted.
    /// </summary>
    /// <param name="json">The document being written.</param>
    /// <param name="plan">The plan whose row count bounds the stream.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    private async Task WriteAuditEntriesAsync(
        Utf8JsonWriter json,
        PersonalDataExportPlan plan,
        CancellationToken cancellationToken)
    {
        var ordered = _auditRows.Of(plan.Subject)
            .OrderBy(auditEvent => auditEvent.OccurredAt)
            .ThenBy(auditEvent => auditEvent.Id);

        json.WriteStartArray("auditEntries");
        long written = 0;
        while (written < plan.AuditRowCount)
        {
            var take = (int)Math.Min(AuditExportLimits.BatchSize, plan.AuditRowCount - written);
            var batch = await ordered
                .Skip((int)written)
                .Take(take)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var auditEvent in batch)
            {
                WriteRow(json, auditEvent);
            }

            written += batch.Count;
            await json.FlushAsync(cancellationToken);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes one journal row as an object, leaving out the chain columns.</summary>
    /// <param name="json">The document being written.</param>
    /// <param name="auditEvent">The row.</param>
    private static void WriteRow(Utf8JsonWriter json, AuditEvent auditEvent)
    {
        var row = JsonSerializer.SerializeToElement(AuditExportRowMapper.From(auditEvent), RowOptions);

        json.WriteStartObject();
        foreach (var property in row.EnumerateObject())
        {
            if (!OmittedColumns.Contains(property.Name))
            {
                property.WriteTo(json);
            }
        }

        json.WriteEndObject();
    }
}
