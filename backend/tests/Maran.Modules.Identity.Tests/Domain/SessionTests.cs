using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;

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

    /// <summary>A new session speaks for no account unless it is given one.</summary>
    [Fact]
    public void A_new_session_speaks_for_no_account_unless_given_one()
    {
        Assert.Null(NewSession().ActiveAccountId);
    }

    /// <summary>Switching changes the account the session speaks for, and clearing it leaves none.</summary>
    [Fact]
    public void Switching_changes_the_account_and_clearing_leaves_none()
    {
        var session = NewSession();
        var accountId = Guid.NewGuid();

        session.SwitchAccount(accountId);
        Assert.Equal(accountId, session.ActiveAccountId);

        session.SwitchAccount(null);
        Assert.Null(session.ActiveAccountId);
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

    /// <summary>A user's own session has no view-as origin.</summary>
    [Fact]
    public void A_users_own_session_has_no_view_as_origin()
    {
        Assert.Null(NewSession().ViewAs);
    }

    /// <summary>A view-as session names the operator and the operator's own session.</summary>
    [Fact]
    public void A_view_as_session_names_the_operator_and_the_operators_own_session()
    {
        var actorUserId = Guid.NewGuid();
        var actorSessionId = Guid.NewGuid();

        var session = Session.StartViewAs(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorUserId,
            actorSessionId,
            "hash",
            Now,
            "203.0.113.7",
            "Mozilla/5.0");

        Assert.Equal(new ViewAsOrigin(actorUserId, actorSessionId), session.ViewAs);
    }
}
