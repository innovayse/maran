using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The database-backed <see cref="ISessionService"/>. Refresh tokens rotate on every use and a
/// replayed token kills its entire lineage (spec §10).
/// </summary>
public sealed class SessionService : ISessionService
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock; never <c>DateTime.UtcNow</c> (rules/csharp.md "Forbidden").</summary>
    private readonly IClock _clock;

    /// <summary>Supplies the refresh-token lifetime.</summary>
    private readonly JwtOptions _options;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="options">The bound <see cref="JwtOptions"/>, read for the refresh lifetime.</param>
    public SessionService(IdentityDbContext dbContext, IClock clock, IOptions<JwtOptions> options)
    {
        _dbContext = dbContext;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IssuedSession> IssueAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        Guid? activeAccountId,
        CancellationToken cancellationToken)
    {
        return await CreateAsync(userId, Guid.NewGuid(), ipAddress, userAgent, activeAccountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<IssuedSession>> RotateAsync(
        string refreshToken,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var hash = RefreshTokenHasher.Hash(refreshToken);
        var session = await _dbContext.Sessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);

        // A view-as session's refresh token never left the server, so presenting one is impossible
        // in honest use; refusing it here as well means that even a leak of the discarded plaintext
        // could not stretch a fifteen-minute view into a renewable one (issue #317).
        if (session is null || session.ViewAs is not null)
        {
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        // A token that has already been rotated is being presented a second time. Either it was
        // stolen and the thief is using it, or it was stolen, used, and the legitimate owner is now
        // presenting their copy — from here the two are indistinguishable, and in both the account
        // is compromised. Revoking the whole family is what makes a stolen cookie usable once
        // rather than for a fortnight, and it tells the real user something is wrong by signing
        // them out.
        if (session.Revocation is not null)
        {
            await RevokeFamilyAsync(session.FamilyId, SessionRevocationReason.ReuseDetected, cancellationToken);
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenReusedUnauthorized), ErrorType.Unauthorized));
        }

        if (!session.IsActive(_clock.UtcNow))
        {
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        session.Revoke(_clock.UtcNow, SessionRevocationReason.Rotated);
        // The account the sign-in speaks for survives a rotation: refreshing is not choosing again.
        var issued = await CreateAsync(
            session.UserId,
            session.FamilyId,
            ipAddress,
            userAgent,
            session.ActiveAccountId,
            cancellationToken);

        return Result<IssuedSession>.Ok(issued);
    }

    /// <inheritdoc />
    public async Task<Guid?> RevokeByRefreshTokenAsync(
        string refreshToken,
        SessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        var hash = RefreshTokenHasher.Hash(refreshToken);
        var session = await _dbContext.Sessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);

        if (session is null)
        {
            return null;
        }

        session.Revoke(_clock.UtcNow, reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return session.UserId;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid sessionId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var session = await _dbContext.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(_clock.UtcNow, reason);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.Sessions
            .Where(s => s.UserId == userId && s.Revocation == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(_clock.UtcNow, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeOthersAsync(
        Guid userId,
        Guid keptSessionId,
        SessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        // Matched on the user too, so a session id belonging to somebody else keeps nothing.
        var keptFamilyId = await _dbContext.Sessions
            .Where(s => s.Id == keptSessionId && s.UserId == userId)
            .Select(s => (Guid?)s.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        var sessions = await _dbContext.Sessions
            .Where(s => s.UserId == userId && s.Revocation == null && s.FamilyId != keptFamilyId)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(_clock.UtcNow, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeSpeakingForAccountAsync(
        Guid accountId,
        SessionRevocationReason reason,
        CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.Sessions
            .Where(s => s.ActiveAccountId == accountId && s.Revocation == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(_clock.UtcNow, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates and stores one session, returning its plaintext token to the caller.</summary>
    /// <param name="userId">The user the session signs in.</param>
    /// <param name="familyId">The rotation family; a new one at login, the inherited one on rotation.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="activeAccountId">The account the session speaks for; null when it has none.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The stored session and its plaintext refresh token.</returns>
    private async Task<IssuedSession> CreateAsync(
        Guid userId,
        Guid familyId,
        string ipAddress,
        string userAgent,
        Guid? activeAccountId,
        CancellationToken cancellationToken)
    {
        var token = RefreshTokenHasher.Generate();
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddDays(_options.RefreshTokenDays);

        var session = new Session(
            Guid.NewGuid(),
            userId,
            familyId,
            RefreshTokenHasher.Hash(token),
            issuedAt,
            expiresAt,
            ipAddress,
            userAgent,
            activeAccountId);

        _dbContext.Sessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedSession(session.Id, token, expiresAt, session.ActiveAccountId);
    }

    /// <summary>Revokes every session in one rotation family.</summary>
    /// <param name="familyId">The family to end.</param>
    /// <param name="reason">Why it is ending.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once every revocation is stored.</returns>
    private async Task RevokeFamilyAsync(Guid familyId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.Sessions
            .Where(s => s.FamilyId == familyId && s.Revocation == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(_clock.UtcNow, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
