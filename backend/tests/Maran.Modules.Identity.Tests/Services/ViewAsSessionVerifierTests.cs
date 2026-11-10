using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the per-request check of a view-as token's session, for both kinds of
/// view-as (issues #317, #179).
/// </summary>
public sealed class ViewAsSessionVerifierTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private async Task<(User Admin, Session AdminSession, User Owner, Session ViewAs)> SeedAsync()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var viewAs = Session.StartViewAs(
            Guid.NewGuid(),
            owner.Id,
            accountId,
            admin.Id,
            adminSession.Id,
            Guid.NewGuid().ToString("N"),
            ViewAsFixture.Now,
            "198.51.100.1",
            "admin-agent");
        _fixture.Context.Sessions.Add(viewAs);
        await _fixture.Context.SaveChangesAsync();
        return (admin, adminSession, owner, viewAs);
    }

    private Task<bool> IsLiveAsync(Session viewAs, Guid viewedUserId, Guid actorUserId, Guid actorSessionId)
    {
        return _fixture.NewVerifier()
            .IsLiveAsync(viewAs.Id, viewedUserId, actorUserId, actorSessionId, CancellationToken.None);
    }

    /// <summary>A fresh view-as session whose operator is still signed in is live.</summary>
    [Fact]
    public async Task A_fresh_session_with_a_signed_in_operator_is_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();

        Assert.True(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>The session stops being live once its lifetime passes.</summary>
    [Fact]
    public async Task An_expired_session_is_not_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(15));

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>Signing the operator out everywhere ends the view-as session too.</summary>
    [Fact]
    public async Task A_revoked_operator_family_ends_the_view_as_session()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id);
        stored.Revoke(ViewAsFixture.Now, SessionRevocationReason.LogoutAll);
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A rotation of the operator's own session keeps the view-as session live.</summary>
    [Fact]
    public async Task A_rotation_of_the_operator_session_keeps_viewing_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id);
        stored.Revoke(ViewAsFixture.Now, SessionRevocationReason.Rotated);
        _fixture.Context.Sessions.Add(new Session(
            Guid.NewGuid(),
            admin.Id,
            stored.FamilyId,
            Guid.NewGuid().ToString("N"),
            ViewAsFixture.Now,
            ViewAsFixture.Now.AddDays(14),
            "198.51.100.1",
            "admin-agent"));
        await _fixture.Context.SaveChangesAsync();

        Assert.True(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A token whose subject or actor disagrees with the row is refused.</summary>
    [Fact]
    public async Task A_token_disagreeing_with_its_row_is_refused()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();

        Assert.False(await IsLiveAsync(viewAs, Guid.NewGuid(), admin.Id, adminSession.Id));
        Assert.False(await IsLiveAsync(viewAs, owner.Id, Guid.NewGuid(), adminSession.Id));
        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, Guid.NewGuid()));
    }

    /// <summary>An own session's id is never accepted as a view-as session.</summary>
    [Fact]
    public async Task An_own_session_is_never_a_live_view_as_session()
    {
        var (admin, adminSession, _, _) = await SeedAsync();

        var verifier = _fixture.NewVerifier();
        Assert.Null(await verifier.FindLiveAsync(adminSession.Id, CancellationToken.None));
        Assert.False(await verifier.IsLiveAsync(adminSession.Id, admin.Id, admin.Id, adminSession.Id, CancellationToken.None));
    }

    /// <summary>A fresh reseller view whose operator is still signed in is live.</summary>
    [Fact]
    public async Task A_fresh_reseller_view_with_a_signed_in_operator_is_live()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);

        Assert.True(await IsLiveAsync(viewAs, login.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A reseller view of a reseller that holds an active account is refused: it describes neither kind.</summary>
    [Fact]
    public async Task A_reseller_session_carrying_an_account_is_not_live()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, Guid.NewGuid());

        Assert.False(await IsLiveAsync(viewAs, login.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A customer view with no active account is refused: it describes neither kind.</summary>
    [Fact]
    public async Task A_customer_session_with_no_account_is_not_live()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, owner) = await _fixture.SeedAccountAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, owner, accountId: null);

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A view-as session of an administrator's login is never live, whatever else holds.</summary>
    [Fact]
    public async Task A_session_of_an_administrator_login_is_never_live()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync("root-admin");
        var (otherAdmin, _) = await _fixture.SeedAdminAsync("other-admin");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, otherAdmin, accountId: null);

        Assert.False(await IsLiveAsync(viewAs, otherAdmin.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A view-as session whose viewed login has been deleted is not live.</summary>
    [Fact]
    public async Task A_session_whose_viewed_login_was_deleted_is_not_live()
    {
        var (admin, adminSession, owner, viewAs) = await SeedAsync();
        _fixture.Context.Users.Remove(owner);
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }

    /// <summary>A view-as session started from inside another view-as session is never live: no chaining.</summary>
    /// <remarks>
    /// The endpoints refuse a view-as token before this is reached; this pins the second wall. The
    /// accepting control is the first assertion: the same session shape with the operator's OWN
    /// session as the actor is live.
    /// </remarks>
    [Fact]
    public async Task A_session_whose_actor_session_is_itself_a_view_as_session_is_not_live()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, resellerLogin) = await _fixture.SeedResellerAsync();
        var (_, owner) = await _fixture.SeedAccountAsync("acme", resellerId);
        var firstHop = await _fixture.AddViewAsSessionAsync(admin, adminSession, resellerLogin, accountId: null);
        var accountId = (await _fixture.Context.AccountMemberships.SingleAsync(m => m.UserId == owner.Id)).AccountId;

        var chained = await _fixture.AddViewAsSessionAsync(resellerLogin, firstHop, owner, accountId);

        Assert.True(await IsLiveAsync(firstHop, resellerLogin.Id, admin.Id, adminSession.Id));
        Assert.False(await IsLiveAsync(chained, owner.Id, resellerLogin.Id, firstHop.Id));
    }

    /// <summary>A reseller operator's own session keeps its view of an account live, and signing it out ends the view.</summary>
    [Fact]
    public async Task A_resellers_view_of_its_account_lives_and_dies_with_the_resellers_own_session()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme", resellerId);
        var viewAs = await _fixture.AddViewAsSessionAsync(login, session, owner, accountId);

        Assert.True(await IsLiveAsync(viewAs, owner.Id, login.Id, session.Id));

        (await _fixture.Context.Sessions.SingleAsync(s => s.Id == session.Id))
            .Revoke(ViewAsFixture.Now, SessionRevocationReason.ResellerSuspended);
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await IsLiveAsync(viewAs, owner.Id, login.Id, session.Id));
    }

    /// <summary>
    /// A reseller's view of an account stops being live the moment the account is another owner's, even
    /// though the session row was never revoked (issue #460). The first assertion is the accepting control:
    /// the same session is live while the reseller still owns the account.
    /// </summary>
    [Fact]
    public async Task A_resellers_view_is_not_live_once_the_reseller_no_longer_owns_the_account()
    {
        var (resellerId, login, session) = await _fixture.SeedResellerOperatorAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme", resellerId);
        var viewAs = await _fixture.AddViewAsSessionAsync(login, session, owner, accountId);
        Assert.True(await IsLiveAsync(viewAs, owner.Id, login.Id, session.Id));

        _fixture.Ownership.HandToAdministrator(accountId);

        Assert.False(await IsLiveAsync(viewAs, owner.Id, login.Id, session.Id));
    }

    /// <summary>An administrator's view of an account is live whoever owns the account.</summary>
    [Fact]
    public async Task An_administrators_view_is_live_whoever_owns_the_account()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, _) = await _fixture.SeedResellerAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme", resellerId);
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, owner, accountId);

        _fixture.Ownership.HandToAdministrator(accountId);

        Assert.True(await IsLiveAsync(viewAs, owner.Id, admin.Id, adminSession.Id));
    }
}
