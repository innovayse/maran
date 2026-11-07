using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ExportAuditEvents;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Queries.ExportAuditEvents;

/// <summary>
/// What an export request decides: how many rows would be written, what the
/// file is called, and that the export is journalled with the administrator who asked before anything is
/// streamed. The search is an <c>ILIKE</c> only PostgreSQL runs, and the row cap needs half a million
/// rows, so both are held by <c>AuditExportEndpointTests</c>.
/// </summary>
public sealed class ExportAuditEventsQueryHandlerTests : IDisposable
{
    /// <summary>The instant the seeded entries count from, and the panel's clock reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 34, 56, TimeSpan.Zero);

    /// <summary>The in-memory journal the handler counts.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Collects the entries the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The administrator who asks.</summary>
    private readonly Guid _administrator = Guid.NewGuid();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>The plan counts exactly the rows the filters keep, and names a CSV file for the clock.</summary>
    [Fact]
    public async Task The_plan_counts_the_rows_the_filters_keep_and_names_a_csv_file_for_the_clock()
    {
        await WriteAsync(Now, IdentityAuditActions.LoginFailed, succeeded: false);
        await WriteAsync(Now, IdentityAuditActions.LoginSucceeded, succeeded: true);

        var result = await HandleAsync(new ExportAuditEventsQuery(
            Format: AuditExportFormat.Csv, Action: FilterValue.Of(IdentityAuditActions.LoginFailed)));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.RowCount);
        Assert.Equal(AuditExportFormat.Csv, result.Value.Format);
        Assert.Equal("maran-audit-20260830T123456Z.csv", result.Value.FileName);
        Assert.Equal("text/csv; charset=utf-8", result.Value.ContentType);
    }

    /// <summary>A JSON export is named and typed as JSON.</summary>
    [Fact]
    public async Task A_json_export_is_named_and_typed_as_json()
    {
        var result = await HandleAsync(new ExportAuditEventsQuery(Format: AuditExportFormat.Json));

        Assert.Equal("maran-audit-20260830T123456Z.json", result.Value.FileName);
        Assert.Equal("application/json", result.Value.ContentType);
        Assert.Equal(0, result.Value.RowCount);
    }

    /// <summary>The window, outcome and account filters narrow the count; the actor filter is an <c>ILIKE</c> held by <c>AuditExportEndpointTests</c>.</summary>
    [Fact]
    public async Task The_window_outcome_and_account_filters_narrow_the_count()
    {
        var account = Guid.NewGuid();
        await WriteAsync(Now.AddDays(-5), "SiteCreated", succeeded: true, actor: "olive", accountId: account);
        await WriteAsync(Now, "SiteCreated", succeeded: false, actor: "olive", accountId: account);
        await WriteAsync(Now, "SiteCreated", succeeded: false, actor: "pepper", accountId: Guid.NewGuid());

        Assert.Equal(3, (await HandleAsync(new ExportAuditEventsQuery())).Value.RowCount);
        Assert.Equal(2, (await HandleAsync(new ExportAuditEventsQuery(From: Now.AddDays(-1)))).Value.RowCount);
        Assert.Equal(2, (await HandleAsync(new ExportAuditEventsQuery(Outcome: AuditOutcome.Failed))).Value.RowCount);
        Assert.Equal(2, (await HandleAsync(new ExportAuditEventsQuery(Account: account))).Value.RowCount);
    }

    /// <summary>An export is journalled as the asking administrator with its format, filters and planned row count.</summary>
    [Fact]
    public async Task An_export_is_journalled_as_the_administrator_with_its_format_filters_and_row_count()
    {
        await WriteAsync(Now, IdentityAuditActions.LoginFailed, succeeded: false);

        await HandleAsync(new ExportAuditEventsQuery(
            Format: AuditExportFormat.Csv,
            Outcome: AuditOutcome.Failed));

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.AuditExported, entry.Action);
        Assert.Equal(_administrator, entry.ActorUserId);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.9", entry.IpAddress);
        Assert.Equal("test-agent", entry.UserAgent);
        Assert.Equal("format=csv rows=1 outcome=failed", entry.Subject);
    }

    /// <summary>Runs the handler with the stamped administrator and address.</summary>
    /// <param name="query">The request, before the controller stamps it.</param>
    /// <returns>The handler's answer.</returns>
    private async Task<Result<AuditExportPlan>> HandleAsync(ExportAuditEventsQuery query)
    {
        var handler = new ExportAuditEventsQueryHandler(
            new AuditEventQuery(_context),
            new IdentityAuditJournal(_audit, new StubCurrentUser(_administrator, "admin")),
            new FakeClock(Now));

        return await handler.HandleAsync(
            query with { RequestedByUserId = _administrator, IpAddress = "203.0.113.9", UserAgent = "test-agent" },
            CancellationToken.None);
    }

    /// <summary>Stores one journal entry.</summary>
    /// <param name="at">When it happened.</param>
    /// <param name="action">What was attempted.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="accountId">The account the entry is about, or null.</param>
    private async Task WriteAsync(
        DateTimeOffset at,
        string action,
        bool succeeded,
        string actor = "olive",
        Guid? accountId = null)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), at, null, actor, action, "subject", "203.0.113.7", "agent", succeeded, null, null, accountId));
        await _context.SaveChangesAsync();
    }
}
