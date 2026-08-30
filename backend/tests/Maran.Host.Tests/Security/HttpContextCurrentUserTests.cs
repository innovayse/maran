using System.Security.Claims;
using Maran.Host.Security;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.Security;

/// <summary>
/// Behavioural contract of <see cref="HttpContextCurrentUser"/>: the one place a verified token
/// becomes the identity every authorization check then trusts.
/// </summary>
/// <remarks>
/// Every case here asks the same question from a different direction — what does this class answer
/// when the claim it wants is absent, malformed, or says something it does not recognise. The
/// answer must always be the one that grants nothing, because a wrong answer is not a bug that
/// shows up as an exception: it is a caller silently treated as somebody else.
/// </remarks>
public sealed class HttpContextCurrentUserTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A signed-in administrator is read out of the token's claims.</summary>
    [Fact]
    public void A_signed_in_administrator_is_read_out_of_the_tokens_claims()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, nameof(UserRole.Admin)));

        Assert.Equal(UserId, user.UserId);
        Assert.True(user.IsAdmin);
        Assert.Null(user.AccountId);
    }

    /// <summary>A customers account is read out of the token's claims.</summary>
    [Fact]
    public void A_customers_account_is_read_out_of_the_tokens_claims()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, nameof(UserRole.Customer)),
            (PanelClaimTypes.AccountId, AccountId.ToString()));

        Assert.Equal(AccountId, user.AccountId);
        Assert.False(user.IsAdmin);
    }

    /// <summary>A token carrying an unknown role is not an administrator.</summary>
    [Fact]
    public void A_token_carrying_an_unknown_role_is_not_an_administrator()
    {
        // An older token, or one forged with a role this build has never heard of. It verified, so
        // it reaches this class — and must still be refused everything an administrator can do.
        var user = CurrentUser((PanelClaimTypes.Role, "SuperAdmin"));

        Assert.False(user.IsAdmin);
    }

    /// <summary>The role comparison is case sensitive.</summary>
    [Fact]
    public void The_role_comparison_is_case_sensitive()
    {
        var user = CurrentUser((PanelClaimTypes.Role, "admin"));

        Assert.False(user.IsAdmin);
    }

    /// <summary>A request with no claims at all is nobody.</summary>
    [Fact]
    public void A_request_with_no_claims_at_all_is_nobody()
    {
        var user = CurrentUser();

        Assert.Equal(Guid.Empty, user.UserId);
        Assert.Null(user.AccountId);
        Assert.False(user.IsAdmin);
    }

    /// <summary>A malformed user identifier reads as empty rather than throwing.</summary>
    [Fact]
    public void A_malformed_user_identifier_reads_as_empty_rather_than_throwing()
    {
        var user = CurrentUser((PanelClaimTypes.UserId, "not-a-guid"));

        Assert.Equal(Guid.Empty, user.UserId);
    }

    /// <summary>A malformed account identifier reads as no account rather than throwing.</summary>
    [Fact]
    public void A_malformed_account_identifier_reads_as_no_account_rather_than_throwing()
    {
        var user = CurrentUser((PanelClaimTypes.AccountId, "not-a-guid"));

        Assert.Null(user.AccountId);
    }

    /// <summary>With no HTTP context at all the user is nobody rather than a crash.</summary>
    [Fact]
    public void With_no_http_context_at_all_the_user_is_nobody_rather_than_a_crash()
    {
        // Background work resolving the scoped service outside a request: there is no context to
        // read, and the honest answer is an identity that owns nothing.
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = null });

        Assert.Equal(Guid.Empty, user.UserId);
        Assert.Null(user.AccountId);
        Assert.False(user.IsAdmin);
    }

    private static HttpContextCurrentUser CurrentUser(params (string Type, string Value)[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(c =>
                {
                    return new Claim(c.Type, c.Value);
                }),
                authenticationType: "Bearer")),
        };

        return new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}
