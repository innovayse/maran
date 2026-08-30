using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
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
        CancellationToken cancellationToken)
    {
        return await CreateAsync(userId, Guid.NewGuid(), ipAddress, userAgent, cancellationToken);
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

        if (session is null)
        {
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized)));
        }

        // A token that has already been rotated is being presented a second time. Either it was
        // stolen and the thief is using it, or it was stolen, used, and the legitimate owner is now
        // presenting their copy — from here the two are indistinguishable, and in both the account
        // is compromised. Revoking the whole family is what makes a stolen cookie usable once
        // rather than for a fortnight, and it tells the real user something is wrong by signing
        // them out.
        if (session.RevokedAt is not null)
        {
            await RevokeFamilyAsync(session.FamilyId, SessionRevocationReason.ReuseDetected, cancellationToken);
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenReusedUnauthorized)));
        }

        if (!session.IsActive(_clock.UtcNow))
        {
            return Result<IssuedSession>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized)));
        }

        session.Revoke(_clock.UtcNow, SessionRevocationReason.Rotated);
        var issued = await CreateAsync(session.UserId, session.FamilyId, ipAddress, userAgent, cancellationToken);

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
            .Where(s => s.UserId == userId && s.RevokedAt == null)
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
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The stored session and its plaintext refresh token.</returns>
    private async Task<IssuedSession> CreateAsync(
        Guid userId,
        Guid familyId,
        string ipAddress,
        string userAgent,
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
            userAgent);

        _dbContext.Sessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedSession(session.Id, token, expiresAt);
    }

    /// <summary>Revokes every session in one rotation family.</summary>
    /// <param name="familyId">The family to end.</param>
    /// <param name="reason">Why it is ending.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once every revocation is stored.</returns>
    private async Task RevokeFamilyAsync(Guid familyId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.Sessions
            .Where(s => s.FamilyId == familyId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(_clock.UtcNow, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
