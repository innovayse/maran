using System.Text;
using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// What <see cref="AuditExportWriter"/> puts on the wire: the download headers, a CSV or JSON body of
/// exactly the rows the plan authorised, oldest first, each once — including across batch boundaries and
/// among entries that share one instant. The search is an <c>ILIKE</c> only PostgreSQL runs, so it is
/// held by <c>AuditExportEndpointTests</c>.
/// </summary>
public sealed class AuditExportWriterTests : IDisposable
{
    /// <summary>The instant the seeded entries count from.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the writer reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A CSV export sets the attachment headers, writes the header then the rows oldest first, and emits no byte-order mark.</summary>
    [Fact]
    public async Task A_csv_export_sets_the_download_headers_and_writes_the_rows_oldest_first_without_a_bom()
    {
        await StoreAsync(Epoch.AddMinutes(2), "second");
        await StoreAsync(Epoch.AddMinutes(1), "first");

        var (response, body) = await WriteAsync(Plan(AuditExportFormat.Csv, 2));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.ContentType);
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal("attachment; filename=\"maran-audit-test.csv\"", response.Headers.ContentDisposition.ToString());
        Assert.NotEqual(0xEF, body[0]);

        var lines = Encoding.UTF8.GetString(body).Split("\r\n");
        Assert.Equal(AuditExportCsvMapper.Header().TrimEnd('\r', '\n'), lines[0]);
        Assert.Contains(",first,", lines[1], StringComparison.Ordinal);
        Assert.Contains(",second,", lines[2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(4, lines.Length);
    }

    /// <summary>A JSON export is one array whose objects carry the CSV header's names, nulls included, and escapes hostile text.</summary>
    [Fact]
    public async Task A_json_export_is_one_array_with_the_csv_header_names_and_escapes_hostile_text()
    {
        await StoreAsync(Epoch, "<script>alert(1)</script>\"");

        var (response, body) = await WriteAsync(Plan(AuditExportFormat.Json, 1));

        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("attachment; filename=\"maran-audit-test.json\"", response.Headers.ContentDisposition.ToString());
        Assert.DoesNotContain("<script>", Encoding.UTF8.GetString(body), StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            AuditExportCsvMapper.Header().TrimEnd('\r', '\n').Split(','),
            row.EnumerateObject().Select(property => { return property.Name; }));
        Assert.Equal("<script>alert(1)</script>\"", row.GetProperty("subject").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("chainIndex").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("accountId").ValueKind);
    }

    /// <summary>With no matching rows a CSV is its header alone and a JSON is an empty array.</summary>
    [Fact]
    public async Task With_no_rows_a_csv_is_its_header_alone_and_a_json_is_an_empty_array()
    {
        var (_, csv) = await WriteAsync(Plan(AuditExportFormat.Csv, 0));
        var (_, json) = await WriteAsync(Plan(AuditExportFormat.Json, 0));

        Assert.Equal(AuditExportCsvMapper.Header(), Encoding.UTF8.GetString(csv));
        Assert.Equal("[]", Encoding.UTF8.GetString(json));
    }

    /// <summary>
    /// The writer stops after the planned row count even though more rows match now: an entry written
    /// while the file streams cannot make it larger than what the handler authorised and journalled.
    /// </summary>
    [Fact]
    public async Task The_writer_stops_after_the_planned_row_count_though_more_rows_match()
    {
        for (var minute = 0; minute < 5; minute++)
        {
            await StoreAsync(Epoch.AddMinutes(minute), $"entry-{minute}");
        }

        var (_, body) = await WriteAsync(Plan(AuditExportFormat.Json, 3));

        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            ["entry-0", "entry-1", "entry-2"],
            document.RootElement.EnumerateArray().Select(row => { return row.GetProperty("subject").GetString(); }));
    }

    /// <summary>The filters of the plan narrow what is written.</summary>
    [Fact]
    public async Task The_plans_filters_narrow_what_is_written()
    {
        await StoreAsync(Epoch.AddMinutes(1), "kept", succeeded: false);
        await StoreAsync(Epoch.AddMinutes(2), "wrong-outcome", succeeded: true);
        await StoreAsync(Epoch, "before-window", succeeded: false);
        var filter = Filter() with { Outcome = AuditOutcome.Failed, From = Epoch.AddMinutes(1) };

        var (_, body) = await WriteAsync(new AuditExportPlan(
            AuditExportFormat.Json, filter, 1, "maran-audit-test.json", "application/json"));

        using var document = JsonDocument.Parse(body);
        Assert.Equal("kept", Assert.Single(document.RootElement.EnumerateArray()).GetProperty("subject").GetString());
    }

    /// <summary>
    /// More rows than one batch, with entries sharing the instant a batch ends on, are written in order
    /// and each exactly once: no row is skipped or repeated at the seam.
    /// </summary>
    [Fact]
    public async Task Rows_across_batches_sharing_the_instant_a_batch_ends_on_are_each_written_exactly_once()
    {
        var first = AuditExportLimits.BatchSize - 300;
        var second = AuditExportLimits.BatchSize + 250;
        var stored = new List<Guid>();
        stored.AddRange(await StoreManyAsync(first, Epoch));
        stored.AddRange(await StoreManyAsync(second, Epoch.AddSeconds(1)));
        stored.AddRange(await StoreManyAsync(10, Epoch.AddSeconds(2)));

        var (_, body) = await WriteAsync(Plan(AuditExportFormat.Json, stored.Count));

        using var document = JsonDocument.Parse(body);
        var written = document.RootElement.EnumerateArray().ToList();
        var ids = written.Select(row => { return row.GetProperty("id").GetGuid(); }).ToList();
        var instants = written.Select(row => { return row.GetProperty("occurredAt").GetString()!; }).ToList();
        Assert.Equal(stored.Count, ids.Count);
        Assert.Equal(stored.Count, ids.Distinct().Count());
        Assert.Equal(stored.Order(), ids.Order());
        Assert.Equal(instants.Order(StringComparer.Ordinal), instants);
    }

    /// <summary>Builds a plan over the whole journal.</summary>
    /// <param name="format">The file format.</param>
    /// <param name="rowCount">How many rows the plan authorises.</param>
    /// <returns>The plan.</returns>
    private static AuditExportPlan Plan(AuditExportFormat format, long rowCount)
    {
        var json = format == AuditExportFormat.Json;
        return new AuditExportPlan(
            format,
            Filter(),
            rowCount,
            json ? "maran-audit-test.json" : "maran-audit-test.csv",
            json ? "application/json" : "text/csv; charset=utf-8");
    }

    /// <summary>The filter with no condition at all.</summary>
    /// <returns>The empty filter.</returns>
    private static AuditEventFilter Filter()
    {
        return new AuditEventFilter(default, null, null, null, default, default, null);
    }

    /// <summary>Runs the writer against a response backed by memory.</summary>
    /// <param name="plan">The plan to write.</param>
    /// <returns>The response, for its headers, and the bytes written to its body.</returns>
    private async Task<(HttpResponse Response, byte[] Body)> WriteAsync(AuditExportPlan plan)
    {
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await new AuditExportWriter(new AuditEventQuery(_context)).WriteAsync(
            context.Response, plan, CancellationToken.None);

        return (context.Response, body.ToArray());
    }

    /// <summary>Stores one journal entry.</summary>
    /// <param name="at">When it happened.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="succeeded">Whether it worked.</param>
    private async Task StoreAsync(DateTimeOffset at, string subject, string actor = "olive", bool succeeded = true)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), at, null, actor, "SiteCreated", subject, "203.0.113.7", "agent", succeeded, null));
        await _context.SaveChangesAsync();
    }

    /// <summary>Stores <paramref name="count"/> entries that all happened at one instant.</summary>
    /// <param name="count">How many entries.</param>
    /// <param name="at">The shared instant.</param>
    /// <returns>The ids stored.</returns>
    private async Task<List<Guid>> StoreManyAsync(int count, DateTimeOffset at)
    {
        var events = Enumerable.Range(0, count)
            .Select(index =>
            {
                return new AuditEvent(Guid.NewGuid(), at, null, "olive", "SiteCreated", "bulk", "203.0.113.7", "agent", true, null);
            })
            .ToList();
        _context.AuditEvents.AddRange(events);
        await _context.SaveChangesAsync();

        return events.Select(auditEvent => { return auditEvent.Id; }).ToList();
    }
}
