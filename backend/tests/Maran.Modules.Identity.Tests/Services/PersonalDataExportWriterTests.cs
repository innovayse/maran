using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the personal-data export writer: the document's shape, its headers, the
/// bound the plan puts on the journal rows, and the chain columns it leaves out (issue #146).
/// </summary>
public sealed class PersonalDataExportWriterTests : IDisposable
{
    /// <summary>The instant the fake clock starts at, and the instant of the first journal row.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The account the tests are about.</summary>
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>The module's context over its own in-memory database.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Stores journal rows for the account, one second apart, the first of them sealed.</summary>
    /// <param name="count">How many rows to store.</param>
    private async Task SeedRowsAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var row = new AuditEvent(
                Guid.NewGuid(), Now.AddSeconds(index), null, "olive", "SiteCreated", $"site-{index}.example.com",
                "192.0.2.1", "ua", true, null, null, AccountId);
            if (index == 0)
            {
                row.Seal(7, new byte[AuditEvent.HashLength], new byte[AuditEvent.SaltLength], new byte[AuditEvent.HashLength]);
            }

            _context.AuditEvents.Add(row);
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>Builds a plan for the account.</summary>
    /// <param name="auditRowCount">The bound the plan states.</param>
    private static PersonalDataExportPlan Plan(long auditRowCount)
    {
        return new PersonalDataExportPlan(
            new PersonalDataSubject(PersonalDataSubjectKind.Account, AccountId),
            [
                new PersonalDataSection("sites", "sites",
                [
                    new PersonalDataRecord(new Dictionary<string, string?>
                    {
                        ["domain"] = "acme.example.com",
                        ["note"] = null,
                    }),
                ]),
            ],
            auditRowCount,
            "maran-personal-data-account-20261107T093000Z.json");
    }

    /// <summary>Runs the writer against a fresh response and parses what it wrote.</summary>
    /// <param name="plan">The plan to write.</param>
    /// <returns>The response, and the parsed document.</returns>
    private async Task<(HttpResponse Response, JsonDocument Document)> WriteAsync(PersonalDataExportPlan plan)
    {
        var http = new DefaultHttpContext();
        var body = new MemoryStream();
        http.Response.Body = body;

        var writer = new PersonalDataExportWriter(
            new PersonalDataAuditRows(new AuditEventQuery(_context)),
            new FakeClock(Now));
        await writer.WriteAsync(http.Response, plan, CancellationToken.None);

        body.Position = 0;
        return (http.Response, await JsonDocument.ParseAsync(body));
    }

    /// <summary>The document carries the subject, the clock's instant, the providers' sections and the journal rows.</summary>
    [Fact]
    public async Task The_document_carries_subject_instant_sections_and_audit_entries()
    {
        await SeedRowsAsync(3);

        var (_, document) = await WriteAsync(Plan(3));
        using (document)
        {
            var root = document.RootElement;
            Assert.Equal("account", root.GetProperty("subject").GetProperty("kind").GetString());
            Assert.Equal(AccountId, root.GetProperty("subject").GetProperty("id").GetGuid());
            Assert.Equal("2026-11-07T09:30:00.0000000Z", root.GetProperty("generatedAt").GetString());
            var section = Assert.Single(root.GetProperty("sections").EnumerateArray());
            Assert.Equal("sites", section.GetProperty("module").GetString());
            var record = Assert.Single(section.GetProperty("records").EnumerateArray());
            Assert.Equal("acme.example.com", record.GetProperty("domain").GetString());
            Assert.Equal(JsonValueKind.Null, record.GetProperty("note").ValueKind);
            Assert.Equal(3, root.GetProperty("auditEntries").GetArrayLength());
        }
    }

    /// <summary>The response is an attachment that must not be cached, named by the plan.</summary>
    [Fact]
    public async Task The_response_is_a_no_store_attachment_named_by_the_plan()
    {
        var (response, document) = await WriteAsync(Plan(0));
        document.Dispose();

        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        var disposition = response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition, StringComparison.Ordinal);
        Assert.Contains("maran-personal-data-account-20261107T093000Z.json", disposition, StringComparison.Ordinal);
    }

    /// <summary>The stream stops at the plan's count, oldest first, even when more rows exist now.</summary>
    [Fact]
    public async Task The_stream_stops_at_the_planned_count_oldest_first()
    {
        await SeedRowsAsync(5);

        var (_, document) = await WriteAsync(Plan(2));
        using (document)
        {
            var subjects = document.RootElement.GetProperty("auditEntries").EnumerateArray()
                .Select(entry =>
                {
                    return entry.GetProperty("subject").GetString();
                })
                .ToList();

            Assert.Equal(["site-0.example.com", "site-1.example.com"], subjects);
        }
    }

    /// <summary>A plan that counted fewer rows than now exist is the proof the bound is the plan's, not the table's.</summary>
    [Fact]
    public async Task A_plan_of_zero_rows_writes_an_empty_entry_list_whatever_the_table_holds()
    {
        await SeedRowsAsync(4);

        var (_, document) = await WriteAsync(Plan(0));
        using (document)
        {
            Assert.Equal(0, document.RootElement.GetProperty("auditEntries").GetArrayLength());
        }
    }

    /// <summary>A journal row is written without its chain columns, even when the row is sealed.</summary>
    /// <remarks>
    /// The first seeded row is sealed, so the columns exist and hold values; the document must still
    /// not name them. The other columns are present, which shows the check could have seen them.
    /// </remarks>
    [Fact]
    public async Task A_sealed_row_is_written_without_its_chain_columns()
    {
        await SeedRowsAsync(1);

        var (_, document) = await WriteAsync(Plan(1));
        using (document)
        {
            var entry = Assert.Single(document.RootElement.GetProperty("auditEntries").EnumerateArray());
            var names = entry.EnumerateObject().Select(property =>
            {
                return property.Name;
            }).ToList();

            Assert.Contains("action", names);
            Assert.Contains("ipAddress", names);
            Assert.Contains("accountId", names);
            Assert.DoesNotContain("chainIndex", names);
            Assert.DoesNotContain("rowHash", names);
        }
    }
}
