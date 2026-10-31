using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>Handles <see cref="ListSessionsQuery"/> by reading one page of the user's live sessions.</summary>
/// <remarks>
/// The owner is the first clause and is applied before the search, the count and the page, so the
/// total is the caller's own device count and no term can reach another user's rows. The search runs
/// over the address and the user agent — the two things that tell one device from another on this
/// screen.
/// </remarks>
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

    /// <summary>Returns one page of the user's unrevoked, unexpired sessions, marking the caller's own.</summary>
    /// <param name="query">Whose sessions to list, which one is the caller's, and the page asked for.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<SessionDto>>> HandleAsync(
        ListSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var sessions = _dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.UserId == query.UserId && session.RevokedAt == null && session.ExpiresAt > now);

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            sessions = sessions.Where(session =>
                EF.Functions.ILike(session.IpAddress, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(session.UserAgent, pattern, LikePattern.EscapeCharacter));
        }

        var page = await sessions.ToPagedAsync(query, ListSessionsSorts.Map, cancellationToken);

        return Result<PagedResult<SessionDto>>.Ok(page.Map(session =>
        {
            return new SessionDto(
                session.Id,
                session.IssuedAt,
                session.ExpiresAt,
                session.IpAddress,
                session.UserAgent,
                session.Id == query.CurrentSessionId);
        }));
    }
}
