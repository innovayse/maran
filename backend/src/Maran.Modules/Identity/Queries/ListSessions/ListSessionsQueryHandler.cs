using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>Handles <see cref="ListSessionsQuery"/> by reading the user's live sessions.</summary>
public sealed class ListSessionsQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock, deciding which sessions are still live.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public ListSessionsQueryHandler(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Returns the user's unrevoked, unexpired sessions, newest first.</summary>
    /// <param name="query">Whose sessions to list, and which one is the caller's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the sessions; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<SessionDto>>> HandleAsync(
        ListSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var sessions = await _dbContext.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == query.UserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.IssuedAt)
            .Select(s => new SessionDto(
                s.Id,
                s.IssuedAt,
                s.ExpiresAt,
                s.IpAddress,
                s.UserAgent,
                s.Id == query.CurrentSessionId))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SessionDto>>.Ok(sessions);
    }
}
