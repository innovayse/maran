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

    /// <summary>An issued token expires fifteen minutes after it was issued.</summary>
    [Fact]
    public async Task An_issued_token_expires_fifteen_minutes_after_it_was_issued()
    {
        var token = await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(Now.AddMinutes(15), token.ExpiresAt);
    }

    /// <summary>An issued token carries the user id username role and session.</summary>
    [Fact]
    public async Task An_issued_token_carries_the_user_id_username_role_and_session()
    {
        var user = NewAdmin();
        var sessionId = Guid.NewGuid();

        var token = await NewIssuer().IssueAsync(user, sessionId, CancellationToken.None);

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
        var token = await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(Read(token.Value).TryGetClaim(PanelClaimTypes.AccountId, out _));
    }

    /// <summary>A customer token carries the account it owns.</summary>
    [Fact]
    public async Task A_customer_token_carries_the_account_it_owns()
    {
        var accountId = Guid.NewGuid();
        var customer = new User(Guid.NewGuid(), "customer", "c@example.com", "hash", UserRole.Customer, Now);
        customer.AssignAccount(accountId);

        var token = await NewIssuer().IssueAsync(customer, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(accountId.ToString(), Read(token.Value).GetClaim(PanelClaimTypes.AccountId).Value);
    }

    /// <summary>An issued token never carries the password hash.</summary>
    [Fact]
    public async Task An_issued_token_never_carries_the_password_hash()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "a-very-secret-hash", UserRole.Admin, Now);

        var token = await NewIssuer().IssueAsync(user, Guid.NewGuid(), CancellationToken.None);

        Assert.DoesNotContain("a-very-secret-hash", token.Value, StringComparison.Ordinal);
    }

    /// <summary>An issued token names the configured issuer and audience.</summary>
    [Fact]
    public async Task An_issued_token_names_the_configured_issuer_and_audience()
    {
        var claims = Read((await NewIssuer().IssueAsync(NewAdmin(), Guid.NewGuid(), CancellationToken.None)).Value);

        Assert.Equal("maran", claims.Issuer);
        Assert.Contains("maran-panel", claims.Audiences);
    }
}
