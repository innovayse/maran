using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListSessions;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Queries.ListSessions;
/// <summary>Behavioural contract of list sessions query handler.</summary>

public sealed class ListSessionsQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private SessionService NewSessionService()
    {
        return new SessionService(_context, _clock, Options.Create(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private ListSessionsQueryHandler NewHandler()
    {
        return new ListSessionsQueryHandler(_context, _clock);
    }

    /// <summary>Listing returns only the callers own sessions.</summary>
    [Fact]
    public async Task Listing_returns_only_the_callers_own_sessions()
    {
        var mine = await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        await NewSessionService().IssueAsync(_stranger, "198.51.100.4", "other", CancellationToken.None);

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(_owner, mine.SessionId), CancellationToken.None);

        Assert.Equal([mine.SessionId], result.Value.Select(s =>
        {
            return s.Id;
        }));
    }

    /// <summary>The session making the request is marked as current.</summary>
    [Fact]
    public async Task The_session_making_the_request_is_marked_as_current()
    {
        var mine = await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        var other = await NewSessionService().IssueAsync(_owner, "198.51.100.4", "phone", CancellationToken.None);

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(_owner, mine.SessionId), CancellationToken.None);

        Assert.True(result.Value.Single(s =>
        {
            return s.Id == mine.SessionId;
        }).IsCurrent);
        Assert.False(result.Value.Single(s =>
        {
            return s.Id == other.SessionId;
        }).IsCurrent);
    }

    /// <summary>A revoked session is not listed.</summary>
    [Fact]
    public async Task A_revoked_session_is_not_listed()
    {
        var service = NewSessionService();
        var issued = await service.IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        await service.RevokeAsync(issued.SessionId, SessionRevocationReason.Logout, CancellationToken.None);

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(_owner, Guid.Empty), CancellationToken.None);

        Assert.Empty(result.Value);
    }

    /// <summary>An expired session is not listed.</summary>
    [Fact]
    public async Task An_expired_session_is_not_listed()
    {
        await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(15));

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(_owner, Guid.Empty), CancellationToken.None);

        Assert.Empty(result.Value);
    }
}
