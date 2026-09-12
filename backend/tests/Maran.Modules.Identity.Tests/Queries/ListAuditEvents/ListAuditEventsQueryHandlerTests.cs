using System.Globalization;
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

    private async Task WriteAsync(DateTimeOffset at, string subject, string action = AuditActions.LoginSucceeded)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), at, null, "admin", action, subject, "203.0.113.7", "agent", true, null));
        await _context.SaveChangesAsync();
    }

    private ListAuditEventsQueryHandler Handler()
    {
        return new ListAuditEventsQueryHandler(_context, IdentityTestContext.ActionNames());
    }

    /// <summary>Listing returns the most recent events first.</summary>
    [Fact]
    public async Task Listing_returns_the_most_recent_events_first()
    {
        await WriteAsync(Now, "first");
        await WriteAsync(Now.AddMinutes(1), "second");

        var result = await Handler().HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

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

        var result = await Handler().HandleAsync(new ListAuditEventsQuery(1), CancellationToken.None);

        Assert.Single(result.Value);
    }

    /// <summary>An empty journal lists nothing rather than failing.</summary>
    [Fact]
    public async Task An_empty_journal_lists_nothing_rather_than_failing()
    {
        var result = await Handler().HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>A row's action reaches the caller named in the request's language beside the machine code.</summary>
    /// <remarks>
    /// The pinned defect: the audit screen showed <c>BackupRestored</c> verbatim in a Russian
    /// interface. The Russian VALUE is asserted, not "some name came back" — and the machine code
    /// stays on the wire beside it, because that constant is what an administrator greps a log by.
    /// </remarks>
    [Fact]
    public async Task A_rows_action_is_named_in_the_requests_language_beside_the_machine_code()
    {
        await WriteAsync(Now, "livecust", AuditActions.BackupRestored);

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var result = await Handler().HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

            var row = Assert.Single(result.Value);
            Assert.Equal("BackupRestored", row.Action);
            Assert.Equal("Аккаунт восстановлен из резервной копии", row.ActionName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>An action this build has no name for reaches the caller as itself, twice.</summary>
    /// <remarks>
    /// The marketplace contract carried through the query: the name falls back to the constant, and
    /// the constant is still on the wire — so an unknown action renders exactly as it did before
    /// names existed, never as a resx key.
    /// </remarks>
    [Fact]
    public async Task An_action_with_no_name_reaches_the_caller_as_the_machine_code()
    {
        await WriteAsync(Now, "livecust", "SomeMarketplaceModuleAction");

        var result = await Handler().HandleAsync(new ListAuditEventsQuery(50), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("SomeMarketplaceModuleAction", row.Action);
        Assert.Equal("SomeMarketplaceModuleAction", row.ActionName);
    }
}
