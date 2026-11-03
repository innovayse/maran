using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of jwt access token issuer.</summary>

public sealed class JwtAccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static JwtAccessTokenIssuer NewIssuer()
    {
        return NewIssuer(TestSecurityPolicyCache.Over(IdentityTestContext.Create()));
    }

    private static JwtAccessTokenIssuer NewIssuer(SecurityPolicyCache policyCache)
    {
        var options = new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            Issuer = "maran",
            Audience = "maran-panel",
            AccessTokenMinutes = 15,
        };

        return new JwtAccessTokenIssuer(new OptionsWrapper<JwtOptions>(options), policyCache, new FakeClock(Now));
    }

    private static User NewAdmin()
    {
        return new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
    }

    private static JsonWebToken Read(string token)
    {
        return new JsonWebTokenHandler().ReadJsonWebToken(token);
    }

    private static User NewReseller(Guid? resellerId = null)
    {
        return User.InviteReseller(Guid.NewGuid(), "northwind", "northwind@example.com", resellerId ?? Guid.NewGuid(), Now);
    }

    private static User NewCustomer()
    {
        return new User(Guid.NewGuid(), "customer", "c@example.com", "hash", UserRole.Customer, Now);
    }

    private static Session NewViewAsSession(User viewed, User actor, Guid? accountId, Guid? actorSessionId = null)
    {
        return Session.StartViewAs(
            Guid.NewGuid(),
            viewed.Id,
            accountId,
            actor.Id,
            actorSessionId ?? Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            Now,
            "198.51.100.1",
            "agent");
    }

    /// <summary>An issued token expires fifteen minutes after it was issued.</summary>
    [Fact]
    public async Task An_issued_token_expires_fifteen_minutes_after_it_was_issued()
    {
        var token = await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal(Now.AddMinutes(15), token.ExpiresAt);
    }

    /// <summary>An issued token carries the user id username role and session.</summary>
    [Fact]
    public async Task An_issued_token_carries_the_user_id_username_role_and_session()
    {
        var user = NewAdmin();
        var sessionId = Guid.NewGuid();

        var token = await NewIssuer().IssueAsync(user, sessionId, null, CancellationToken.None);

        var claims = Read(token.Value);
        Assert.Equal(user.Id.ToString(), claims.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal("admin", claims.GetClaim(PanelClaimTypes.Username).Value);
        Assert.Equal(nameof(UserRole.Admin), claims.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(sessionId.ToString(), claims.GetClaim(PanelClaimTypes.SessionId).Value);
    }

    /// <summary>An administrator token carries no account claim at all.</summary>
    [Fact]
    public async Task An_administrator_token_carries_no_account_claim_at_all()
    {
        var token = await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), null, CancellationToken.None);

        Assert.False(Read(token.Value).TryGetClaim(PanelClaimTypes.AccountId, out _));
    }

    /// <summary>A customer token carries the account the session is acting for.</summary>
    [Fact]
    public async Task A_customer_token_carries_the_account_the_session_is_acting_for()
    {
        var accountId = Guid.NewGuid();
        var customer = new User(Guid.NewGuid(), "customer", "c@example.com", "hash", UserRole.Customer, Now);
        // The claim follows the session's active account; a login row names no account of its own.

        var token = await NewIssuer().IssueAsync(customer, Guid.NewGuid(), accountId, CancellationToken.None);

        Assert.Equal(accountId.ToString(), Read(token.Value).GetClaim(PanelClaimTypes.AccountId).Value);
    }

    /// <summary>An issued token never carries the password hash.</summary>
    [Fact]
    public async Task An_issued_token_never_carries_the_password_hash()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "a-very-secret-hash", UserRole.Admin, Now);

        var token = await NewIssuer().IssueAsync(user, Guid.NewGuid(), null, CancellationToken.None);

        Assert.DoesNotContain("a-very-secret-hash", token.Value, StringComparison.Ordinal);
    }

    /// <summary>An issued token names the configured issuer and audience.</summary>
    [Fact]
    public async Task An_issued_token_names_the_configured_issuer_and_audience()
    {
        var claims = Read((await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), null, CancellationToken.None)).Value);

        Assert.Equal("maran", claims.Issuer);
        Assert.Contains("maran-panel", claims.Audiences);
    }

    /// <summary>A reseller token carries the Reseller role and the reseller id, and no account claim.</summary>
    [Fact]
    public async Task A_reseller_token_carries_the_role_and_the_reseller_id_and_no_account()
    {
        var resellerId = Guid.NewGuid();
        var reseller = NewReseller(resellerId);

        var token = await NewIssuer().IssueAsync(reseller, Guid.NewGuid(), null, CancellationToken.None);

        var claims = Read(token.Value);
        Assert.Equal(nameof(UserRole.Reseller), claims.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(resellerId.ToString(), claims.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.False(claims.TryGetClaim(PanelClaimTypes.AccountId, out _));
    }

    /// <summary>Only a reseller token carries the reseller claim: an administrator's and a customer's do not.</summary>
    [Fact]
    public async Task Only_a_reseller_token_carries_the_reseller_claim()
    {
        var issuer = NewIssuer();

        var admin = await issuer.IssueAsync(NewAdmin(), Guid.NewGuid(), null, CancellationToken.None);
        var customer = await issuer.IssueAsync(NewCustomer(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(Read(admin.Value).TryGetClaim(PanelClaimTypes.ResellerId, out _));
        Assert.False(Read(customer.Value).TryGetClaim(PanelClaimTypes.ResellerId, out _));
    }

    /// <summary>A reseller login naming an active account is refused: a token never names both.</summary>
    [Fact]
    public async Task A_reseller_token_for_a_session_with_an_account_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewIssuer().IssueAsync(NewReseller(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        });
    }

    /// <summary>A reseller role with no reseller id on the login is refused rather than signed.</summary>
    [Fact]
    public async Task A_reseller_login_without_a_reseller_id_is_refused()
    {
        var broken = new User(Guid.NewGuid(), "northwind", "n@example.com", "hash", UserRole.Reseller, Now);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewIssuer().IssueAsync(broken, Guid.NewGuid(), null, CancellationToken.None);
        });
    }

    /// <summary>
    /// A reseller without a second factor is steered into enrolment by default: the token carries the
    /// steering claim and says so (decision D4).
    /// </summary>
    [Fact]
    public async Task A_reseller_without_two_factor_is_steered_into_enrolment_by_default()
    {
        var token = await NewIssuer().IssueAsync(NewReseller(), Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(token.RequiresTwoFactorSetup);
        Assert.Equal("true", Read(token.Value).GetClaim(PanelClaimTypes.TwoFactorSetupRequired).Value);
    }

    /// <summary>A reseller who has enrolled is not steered.</summary>
    [Fact]
    public async Task A_reseller_with_two_factor_is_not_steered()
    {
        var reseller = NewReseller();
        reseller.EnableTotp("JBSWY3DPEHPK3PXP");

        var token = await NewIssuer().IssueAsync(reseller, Guid.NewGuid(), null, CancellationToken.None);

        Assert.False(token.RequiresTwoFactorSetup);
        Assert.False(Read(token.Value).TryGetClaim(PanelClaimTypes.TwoFactorSetupRequired, out _));
    }

    /// <summary>The reseller switch and the administrator switch are independent, in both directions.</summary>
    /// <remarks>
    /// All four combinations for each of the two roles, so a role that read the OTHER role's switch
    /// fails: the policy values differ between the two roles in every off-diagonal row.
    /// </remarks>
    /// <param name="forAdmins">The administrators' switch.</param>
    /// <param name="forResellers">The resellers' switch.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Each_roles_steering_follows_only_its_own_switch(bool forAdmins, bool forResellers)
    {
        using var context = IdentityTestContext.Create();
        var issuer = NewIssuer(TestSecurityPolicyCache.Saved(
            context,
            forceTwoFactorForAdmins: forAdmins,
            forceTwoFactorForResellers: forResellers));

        var admin = await issuer.IssueAsync(NewAdmin(), Guid.NewGuid(), null, CancellationToken.None);
        var reseller = await issuer.IssueAsync(NewReseller(), Guid.NewGuid(), null, CancellationToken.None);
        var customer = await issuer.IssueAsync(NewCustomer(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(forAdmins, admin.RequiresTwoFactorSetup);
        Assert.Equal(forResellers, reseller.RequiresTwoFactorSetup);
        Assert.False(customer.RequiresTwoFactorSetup);
    }

    /// <summary>An account view-as token carries the viewed customer's role, the account, the session kind and the operator.</summary>
    [Fact]
    public void An_account_view_as_token_carries_the_viewed_role_the_account_and_the_operator()
    {
        var admin = NewAdmin();
        var owner = NewCustomer();
        var accountId = Guid.NewGuid();
        var actorSessionId = Guid.NewGuid();
        var session = NewViewAsSession(owner, admin, accountId, actorSessionId);

        var token = NewIssuer().IssueViewAs(owner, session, admin);

        var claims = Read(token.Value);
        Assert.Equal(owner.Id.ToString(), claims.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal(nameof(UserRole.Customer), claims.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(accountId.ToString(), claims.GetClaim(PanelClaimTypes.AccountId).Value);
        Assert.False(claims.TryGetClaim(PanelClaimTypes.ResellerId, out _));
        Assert.Equal("ViewAs", claims.GetClaim(PanelClaimTypes.SessionKind).Value);
        Assert.Equal(session.Id.ToString(), claims.GetClaim(PanelClaimTypes.SessionId).Value);
        using var actor = JsonDocument.Parse(claims.GetClaim(PanelClaimTypes.Actor).Value);
        Assert.Equal(admin.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());
        Assert.Equal(actorSessionId.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.SessionId).GetString());
    }

    /// <summary>A reseller view-as token carries role Reseller and the reseller id, never an account or Admin.</summary>
    [Fact]
    public void A_reseller_view_as_token_carries_role_reseller_and_the_reseller_id()
    {
        var admin = NewAdmin();
        var resellerId = Guid.NewGuid();
        var reseller = NewReseller(resellerId);
        var session = NewViewAsSession(reseller, admin, accountId: null);

        var token = NewIssuer().IssueViewAs(reseller, session, admin);

        var claims = Read(token.Value);
        Assert.Equal(reseller.Id.ToString(), claims.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal(nameof(UserRole.Reseller), claims.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(resellerId.ToString(), claims.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.False(claims.TryGetClaim(PanelClaimTypes.AccountId, out _));
        Assert.Equal("ViewAs", claims.GetClaim(PanelClaimTypes.SessionKind).Value);
        using var actor = JsonDocument.Parse(claims.GetClaim(PanelClaimTypes.Actor).Value);
        Assert.Equal(admin.Id.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());
        Assert.DoesNotContain(nameof(UserRole.Admin), claims.Claims.Where(c => c.Type == PanelClaimTypes.Role).Select(c => c.Value));
    }

    /// <summary>A view-as token expires at the instant its session does, for both kinds.</summary>
    [Fact]
    public void A_view_as_token_expires_with_its_session_for_both_kinds()
    {
        var admin = NewAdmin();
        var owner = NewCustomer();
        var reseller = NewReseller();
        var accountSession = NewViewAsSession(owner, admin, Guid.NewGuid());
        var resellerSession = NewViewAsSession(reseller, admin, accountId: null);
        var issuer = NewIssuer();

        Assert.Equal(accountSession.ExpiresAt, issuer.IssueViewAs(owner, accountSession, admin).ExpiresAt);
        Assert.Equal(resellerSession.ExpiresAt, issuer.IssueViewAs(reseller, resellerSession, admin).ExpiresAt);
    }

    /// <summary>A view-as token is never steered into enrolment, even when the policy forces it and the viewed login has none.</summary>
    [Fact]
    public void A_view_as_token_is_never_steered_into_two_factor_enrolment()
    {
        using var context = IdentityTestContext.Create();
        var issuer = NewIssuer(TestSecurityPolicyCache.Saved(
            context,
            forceTwoFactorForAdmins: true,
            forceTwoFactorForResellers: true));
        var admin = NewAdmin();
        var reseller = NewReseller();
        var session = NewViewAsSession(reseller, admin, accountId: null);

        var token = issuer.IssueViewAs(reseller, session, admin);

        Assert.False(token.RequiresTwoFactorSetup);
        Assert.False(Read(token.Value).TryGetClaim(PanelClaimTypes.TwoFactorSetupRequired, out _));
    }

    /// <summary>A reseller session that names an account is refused: it describes neither kind.</summary>
    [Fact]
    public void A_reseller_view_as_session_with_an_account_throws()
    {
        var admin = NewAdmin();
        var reseller = NewReseller();
        var session = NewViewAsSession(reseller, admin, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
        {
            NewIssuer().IssueViewAs(reseller, session, admin);
        });
    }

    /// <summary>A reseller login with no reseller id cannot be viewed as: the token would name no reseller.</summary>
    [Fact]
    public void A_reseller_view_of_a_login_with_no_reseller_id_throws()
    {
        var admin = NewAdmin();
        var broken = new User(Guid.NewGuid(), "northwind", "n@example.com", "hash", UserRole.Reseller, Now);
        var session = NewViewAsSession(broken, admin, accountId: null);

        Assert.Throws<InvalidOperationException>(() =>
        {
            NewIssuer().IssueViewAs(broken, session, admin);
        });
    }

    /// <summary>A customer session with no active account is refused: the token would name no account.</summary>
    [Fact]
    public void A_customer_view_as_session_with_no_account_throws()
    {
        var admin = NewAdmin();
        var owner = NewCustomer();
        var session = NewViewAsSession(owner, admin, accountId: null);

        Assert.Throws<InvalidOperationException>(() =>
        {
            NewIssuer().IssueViewAs(owner, session, admin);
        });
    }

    /// <summary>An administrator's login is never viewed as: no token is signed for it.</summary>
    [Fact]
    public void A_view_as_token_for_an_administrator_login_is_never_signed()
    {
        var admin = NewAdmin();
        var otherAdmin = NewAdmin();
        var session = NewViewAsSession(otherAdmin, admin, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
        {
            NewIssuer().IssueViewAs(otherAdmin, session, admin);
        });
    }

    /// <summary>A session that is not a view-as session, or belongs to another login or another operator, is refused.</summary>
    [Fact]
    public void A_session_that_is_not_a_view_as_session_of_these_two_users_throws()
    {
        var admin = NewAdmin();
        var owner = NewCustomer();
        var issuer = NewIssuer();
        var own = new Session(Guid.NewGuid(), owner.Id, Guid.NewGuid(), "hash", Now, Now.AddDays(1), "198.51.100.1", "agent", Guid.NewGuid());
        var forSomebodyElse = NewViewAsSession(NewCustomer(), admin, Guid.NewGuid());
        var startedByAnother = NewViewAsSession(owner, NewAdmin(), Guid.NewGuid());
        var good = NewViewAsSession(owner, admin, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => { issuer.IssueViewAs(owner, own, admin); });
        Assert.Throws<InvalidOperationException>(() => { issuer.IssueViewAs(owner, forSomebodyElse, admin); });
        Assert.Throws<InvalidOperationException>(() => { issuer.IssueViewAs(owner, startedByAnother, admin); });
        Assert.NotEmpty(issuer.IssueViewAs(owner, good, admin).Value);
    }
}
