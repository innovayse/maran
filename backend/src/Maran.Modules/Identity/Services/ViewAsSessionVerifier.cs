using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Answers, on every request a view-as token makes, whether the view-as session behind it may still
/// be used (issue #317).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a view-as token is not stateless like every other access token.</b> An ordinary token is
/// accepted for its fifteen minutes whatever happens to its session, which is a bounded, stated cost.
/// A view-as token is a credential for SOMEBODY ELSE, so Back, the viewed customer ending it from
/// their sessions screen, a suspension, and the operator signing out everywhere must each end it at
/// once rather than up to fifteen minutes later. The bearer handler therefore asks this on every
/// request carrying a view-as marker, and a "no" is a failed authentication.
/// </para>
/// <para>
/// <b>What "live" means, every clause required:</b> the row named by the token's <c>sid</c> exists,
/// is a <see cref="SessionKind.ViewAs"/> session, is active, belongs to the token's <c>sub</c>, and
/// names the token's <c>act</c> user as its actor; AND the operator's own rotation family — found
/// from <see cref="Session.ActorSessionId"/> — still holds an active session of that operator. The
/// FAMILY and not the row, because the operator's row rotates whenever another of their tabs
/// refreshes, and checking the row would end viewing at every rotation.
/// </para>
/// </remarks>
public sealed class ViewAsSessionVerifier
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the verifier.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public ViewAsSessionVerifier(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Reports whether a view-as token's session may still be used.</summary>
    /// <param name="sessionId">The token's <c>sid</c>.</param>
    /// <param name="viewedUserId">The token's <c>sub</c>.</param>
    /// <param name="actorUserId">The user its <c>act</c> claim names.</param>
    /// <param name="actorSessionId">The session its <c>act</c> claim names.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>True only when every clause in the type's remarks holds.</returns>
    public async Task<bool> IsLiveAsync(
        Guid sessionId,
        Guid viewedUserId,
        Guid actorUserId,
        Guid actorSessionId,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var session = await FindLiveAsync(sessionId, cancellationToken);

        if (session is null
            || session.UserId != viewedUserId
            || session.ViewAs != new ViewAsOrigin(actorUserId, actorSessionId))
        {
            return false;
        }

        var actorFamilyId = await _dbContext.Sessions
            .AsNoTracking()
            .Where(s => s.Id == actorSessionId && s.UserId == actorUserId && s.Kind == SessionKind.Own)
            .Select(s => (Guid?)s.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (actorFamilyId is null)
        {
            return false;
        }

        return await _dbContext.Sessions
            .AsNoTracking()
            .AnyAsync(
                s => s.FamilyId == actorFamilyId && s.UserId == actorUserId && s.RevokedAt == null && s.ExpiresAt > now,
                cancellationToken);
    }

    /// <summary>Reads a view-as session that is still active.</summary>
    /// <param name="sessionId">The session's id.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The session, or null when there is no such view-as session or it has ended.</returns>
    public async Task<Session?> FindLiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        return await _dbContext.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == sessionId && s.Kind == SessionKind.ViewAs && s.RevokedAt == null && s.ExpiresAt > now,
                cancellationToken);
    }
}
