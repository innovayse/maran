using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditEvents;
/// <summary>Behavioural contract of list audit events query handler.</summary>

public sealed class ListAuditEventsQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task WriteAsync(DateTimeOffset at, string subject)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), at, null, "admin", AuditActions.LoginSucceeded, subject, "203.0.113.7", "agent", true, null));
        await _context.SaveChangesAsync();
    }

    /// <summary>Listing returns the most recent events first.</summary>
    [Fact]
    public async Task Listing_returns_the_most_recent_events_first()
    {
        await WriteAsync(Now, "first");
        await WriteAsync(Now.AddMinutes(1), "second");

        var result = await new ListAuditEventsQueryHandler(_context).HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

        Assert.Equal(["second", "first"], result.Value.Select(e =>
        {
            return e.Subject;
        }));
    }

    /// <summary>Listing returns no more rows than the limit asks for.</summary>
    [Fact]
    public async Task Listing_returns_no_more_rows_than_the_limit_asks_for()
    {
        await WriteAsync(Now, "first");
        await WriteAsync(Now.AddMinutes(1), "second");

        var result = await new ListAuditEventsQueryHandler(_context).HandleAsync(new ListAuditEventsQuery(1), CancellationToken.None);

        Assert.Single(result.Value);
    }

    /// <summary>An empty journal lists nothing rather than failing.</summary>
    [Fact]
    public async Task An_empty_journal_lists_nothing_rather_than_failing()
    {
        var result = await new ListAuditEventsQueryHandler(_context).HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
