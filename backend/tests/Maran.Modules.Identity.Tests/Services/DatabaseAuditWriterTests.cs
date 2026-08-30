using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of database audit writer.</summary>

public sealed class DatabaseAuditWriterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private DatabaseAuditWriter NewWriter()
    {
        return new DatabaseAuditWriter(_context, new FakeClock(Now), new FakeCorrelationIdAccessor("correlation-1"));
    }

    /// <summary>A written entry is stamped with the clock and the correlation id.</summary>
    [Fact]
    public async Task A_written_entry_is_stamped_with_the_clock_and_the_correlation_id()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(Guid.NewGuid(), "admin", AuditActions.LoginSucceeded, "admin", "203.0.113.7", "agent", Succeeded: true),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Equal(Now, stored.OccurredAt);
        Assert.Equal("correlation-1", stored.CorrelationId);
    }

    /// <summary>A failed login is recorded with no actor but with the attempted username.</summary>
    [Fact]
    public async Task A_failed_login_is_recorded_with_no_actor_but_with_the_attempted_username()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(null, "nosuchuser", AuditActions.LoginFailed, "nosuchuser", "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Null(stored.ActorUserId);
        Assert.Equal("nosuchuser", stored.ActorUsername);
        Assert.False(stored.Succeeded);
    }

    /// <summary>An entry written outside a request carries no correlation id rather than failing.</summary>
    [Fact]
    public async Task An_entry_written_outside_a_request_carries_no_correlation_id_rather_than_failing()
    {
        var writer = new DatabaseAuditWriter(_context, new FakeClock(Now), new FakeCorrelationIdAccessor(null));

        await writer.WriteAsync(
            new AuditEntry(null, "system", AuditActions.LoginFailed, "system", "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        Assert.Null((await _context.AuditEvents.SingleAsync()).CorrelationId);
    }
}
