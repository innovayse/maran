using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Queries.GetProfile;

/// <summary>Handles <see cref="GetProfileQuery"/> by reading the user and counting their live sessions.</summary>
public sealed class GetProfileQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock, deciding which sessions are still live.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public GetProfileQueryHandler(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Returns the caller's profile.</summary>
    /// <param name="query">Whose profile to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The profile, or <c>UserNotFound</c> when the user behind a still-valid token has since been
    /// deleted.
    /// </returns>
    public async Task<Result<ProfileDto>> HandleAsync(GetProfileQuery query, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);

        if (user is null)
        {
            return Result<ProfileDto>.Fail(Error.Of(nameof(ErrorMessages.UserNotFound), ErrorType.NotFound));
        }

        // The same "live" the sessions screen lists — unrevoked and unexpired — so the count on the
        // profile and the rows one click away can never disagree.
        var now = _clock.UtcNow;
        var activeSessions = await _dbContext.Sessions
            .AsNoTracking()
            .CountAsync(
                session => session.UserId == user.Id && session.RevokedAt == null && session.ExpiresAt > now,
                cancellationToken);

        return Result<ProfileDto>.Ok(new ProfileDto(
            user.Username,
            user.Email,
            user.Role,
            user.IsTotpEnabled,
            activeSessions));
    }
}
