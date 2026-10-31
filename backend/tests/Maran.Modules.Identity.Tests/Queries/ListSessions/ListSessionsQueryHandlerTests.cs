using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListSessions;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Queries.ListSessions;

/// <summary>Behavioural contract of list sessions query handler.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>SessionListEndpointTests</c> covers it against a real server.
/// </remarks>
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
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
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

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(UserId: _owner, CurrentSessionId: mine.SessionId), CancellationToken.None);

        Assert.Equal([mine.SessionId], result.Value.Items.Select(s =>
        {
            return s.Id;
        }));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>The session making the request is marked as current.</summary>
    [Fact]
    public async Task The_session_making_the_request_is_marked_as_current()
    {
        var mine = await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        var other = await NewSessionService().IssueAsync(_owner, "198.51.100.4", "phone", CancellationToken.None);

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(UserId: _owner, CurrentSessionId: mine.SessionId), CancellationToken.None);

        Assert.True(result.Value.Items.Single(s =>
        {
            return s.Id == mine.SessionId;
        }).IsCurrent);
        Assert.False(result.Value.Items.Single(s =>
        {
            return s.Id == other.SessionId;
        }).IsCurrent);
    }

    /// <summary>
    /// The owner is applied before the count and the page: another user's sessions add nothing to the
    /// total, and the pages split only the caller's own, newest first.
    /// </summary>
    [Fact]
    public async Task The_total_and_the_pages_are_of_the_callers_own_sessions_newest_first()
    {
        var issued = new List<Guid>();
        for (var index = 0; index < 11; index++)
        {
            issued.Add((await NewSessionService().IssueAsync(
                _owner, "203.0.113.7", $"device{index}", CancellationToken.None)).SessionId);
            await NewSessionService().IssueAsync(_stranger, "198.51.100.4", "other", CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var first = await NewHandler().HandleAsync(
            new ListSessionsQuery(PageSize: 10, UserId: _owner), CancellationToken.None);
        var second = await NewHandler().HandleAsync(
            new ListSessionsQuery(Page: 2, PageSize: 10, UserId: _owner), CancellationToken.None);

        Assert.Equal(11, first.Value.Total);
        Assert.Equal(11, second.Value.Total);
        Assert.Equal(issued[10], first.Value.Items[0].Id);
        Assert.Equal([issued[0]], second.Value.Items.Select(s =>
        {
            return s.Id;
        }));
    }

    /// <summary>Sorting by expiry ascending puts the session that ends soonest first.</summary>
    [Fact]
    public async Task Sorting_by_expiry_ascending_puts_the_session_that_ends_soonest_first()
    {
        var older = await NewSessionService().IssueAsync(_owner, "203.0.113.7", "older", CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));
        var newer = await NewSessionService().IssueAsync(_owner, "203.0.113.8", "newer", CancellationToken.None);

        var result = await NewHandler().HandleAsync(
            new ListSessionsQuery(Sort: SortRequest.Parse(SessionSortFields.ExpiresAt), UserId: _owner),
            CancellationToken.None);

        Assert.Equal([older.SessionId, newer.SessionId], result.Value.Items.Select(s =>
        {
            return s.Id;
        }));
    }

    /// <summary>A revoked session is not listed.</summary>
    [Fact]
    public async Task A_revoked_session_is_not_listed()
    {
        var service = NewSessionService();
        var issued = await service.IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        await service.RevokeAsync(issued.SessionId, SessionRevocationReason.Logout, CancellationToken.None);

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(UserId: _owner), CancellationToken.None);

        Assert.Empty(result.Value.Items);
    }

    /// <summary>An expired session is not listed.</summary>
    [Fact]
    public async Task An_expired_session_is_not_listed()
    {
        await NewSessionService().IssueAsync(_owner, "203.0.113.7", "agent", CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(15));

        var result = await NewHandler().HandleAsync(new ListSessionsQuery(UserId: _owner), CancellationToken.None);

        Assert.Empty(result.Value.Items);
    }
}
