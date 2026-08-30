using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
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
        var options = new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            Issuer = "maran",
            Audience = "maran-panel",
            AccessTokenMinutes = 15,
        };

        return new JwtAccessTokenIssuer(Options.Create(options), new FakeClock(Now));
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
    public void An_issued_token_expires_fifteen_minutes_after_it_was_issued()
    {
        var token = NewIssuer().Issue(NewAdmin(), Guid.NewGuid());

        Assert.Equal(Now.AddMinutes(15), token.ExpiresAt);
    }

    /// <summary>An issued token carries the user id username role and session.</summary>
    [Fact]
    public void An_issued_token_carries_the_user_id_username_role_and_session()
    {
        var user = NewAdmin();
        var sessionId = Guid.NewGuid();

        var token = NewIssuer().Issue(user, sessionId);

        var claims = Read(token.Value);
        Assert.Equal(user.Id.ToString(), claims.GetClaim(PanelClaimTypes.UserId).Value);
        Assert.Equal("admin", claims.GetClaim(PanelClaimTypes.Username).Value);
        Assert.Equal(nameof(UserRole.Admin), claims.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(sessionId.ToString(), claims.GetClaim(PanelClaimTypes.SessionId).Value);
    }

    /// <summary>An administrator token carries no account claim at all.</summary>
    [Fact]
    public void An_administrator_token_carries_no_account_claim_at_all()
    {
        var token = NewIssuer().Issue(NewAdmin(), Guid.NewGuid());

        Assert.False(Read(token.Value).TryGetClaim(PanelClaimTypes.AccountId, out _));
    }

    /// <summary>A customer token carries the account it owns.</summary>
    [Fact]
    public void A_customer_token_carries_the_account_it_owns()
    {
        var accountId = Guid.NewGuid();
        var customer = new User(Guid.NewGuid(), "customer", "c@example.com", "hash", UserRole.Customer, Now);
        customer.AssignAccount(accountId);

        var token = NewIssuer().Issue(customer, Guid.NewGuid());

        Assert.Equal(accountId.ToString(), Read(token.Value).GetClaim(PanelClaimTypes.AccountId).Value);
    }

    /// <summary>An issued token never carries the password hash.</summary>
    [Fact]
    public void An_issued_token_never_carries_the_password_hash()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "a-very-secret-hash", UserRole.Admin, Now);

        var token = NewIssuer().Issue(user, Guid.NewGuid());

        Assert.DoesNotContain("a-very-secret-hash", token.Value, StringComparison.Ordinal);
    }

    /// <summary>An issued token names the configured issuer and audience.</summary>
    [Fact]
    public void An_issued_token_names_the_configured_issuer_and_audience()
    {
        var claims = Read(NewIssuer().Issue(NewAdmin(), Guid.NewGuid()).Value);

        Assert.Equal("maran", claims.Issuer);
        Assert.Contains("maran-panel", claims.Audiences);
    }
}
