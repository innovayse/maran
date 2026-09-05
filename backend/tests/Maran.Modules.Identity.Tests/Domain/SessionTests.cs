using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Tests.Domain;
/// <summary>Behavioural contract of session.</summary>

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static Session NewSession()
    {
        return new Session(
            Guid.NewGuid(),
            userId: Guid.NewGuid(),
            familyId: Guid.NewGuid(),
            tokenHash: "hash",
            issuedAt: Now,
            expiresAt: Now.AddDays(14),
            ipAddress: "203.0.113.7",
            userAgent: "Mozilla/5.0");
    }

    /// <summary>A new session is active.</summary>
    [Fact]
    public void A_new_session_is_active()
    {
        Assert.True(NewSession().IsActive(Now));
    }

    /// <summary>A revoked session is not active.</summary>
    [Fact]
    public void A_revoked_session_is_not_active()
    {
        var session = NewSession();

        session.Revoke(Now.AddMinutes(5), SessionRevocationReason.Logout);

        Assert.False(session.IsActive(Now.AddMinutes(6)));
    }

    /// <summary>An expired session is not active even though it was never revoked.</summary>
    [Fact]
    public void An_expired_session_is_not_active_even_though_it_was_never_revoked()
    {
        Assert.False(NewSession().IsActive(Now.AddDays(15)));
    }

    /// <summary>Revoking twice keeps the first reason and instant.</summary>
    [Fact]
    public void Revoking_twice_keeps_the_first_reason_and_instant()
    {
        var session = NewSession();
        session.Revoke(Now.AddMinutes(5), SessionRevocationReason.Rotated);

        session.Revoke(Now.AddMinutes(9), SessionRevocationReason.ReuseDetected);

        Assert.Equal(Now.AddMinutes(5), session.RevokedAt);
        Assert.Equal(SessionRevocationReason.Rotated, session.RevocationReason);
    }
}
