using System.Security.Claims;
using System.Text.Json;
using Maran.Host.Security;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.Security;

/// <summary>
/// Behavioural contract of <see cref="HttpContextCurrentUser"/>: the one place a verified token
/// becomes the caller every authorization check then trusts.
/// </summary>
/// <remarks>
/// A request with nobody signed in is an anonymous caller. A signed-in request is an administrator
/// or a customer (with or without an account), read exactly. Anything else about a signed-in request —
/// a role this build does not know, a missing or unreadable id, a damaged account claim — is refused with
/// <see cref="UnrecognisedPrincipalException"/> and is never folded into anonymous or a customer,
/// because that would be a caller silently treated as somebody else.
/// </remarks>
public sealed class HttpContextCurrentUserTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OperatorSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>A signed-in administrator is read out of the token's claims.</summary>
    [Fact]
    public void A_signed_in_administrator_is_read_out_of_the_tokens_claims()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Username, "root"),
            (PanelClaimTypes.Role, nameof(UserRole.Admin)));

        var administrator = Assert.IsType<AdministratorCaller>(user.Caller);
        Assert.Equal(UserId, administrator.UserId);
        Assert.Equal("root", administrator.Username);
        Assert.Null(administrator.ViewedBy);
        Assert.True(user.IsAdmin);
        Assert.Null(user.AccountId);
    }

    /// <summary>A customer's account is read out of the token's claims.</summary>
    [Fact]
    public void A_customers_account_is_read_out_of_the_tokens_claims()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Username, "acme"),
            (PanelClaimTypes.Role, nameof(UserRole.Customer)),
            (PanelClaimTypes.AccountId, AccountId.ToString()));

        var customer = Assert.IsType<CustomerCaller>(user.Caller);
        Assert.Equal(UserId, customer.UserId);
        Assert.Equal("acme", customer.Username);
        Assert.Equal(AccountId, customer.AccountId);
        Assert.Null(customer.ViewedBy);
        Assert.False(user.IsAdmin);
    }

    /// <summary>A view-as token names the operator while the viewed login stays the caller.</summary>
    [Fact]
    public void A_view_as_token_names_the_operator_while_the_viewed_login_stays_the_caller()
    {
        var actor = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [PanelClaimTypes.UserId] = OperatorId.ToString(),
            [PanelClaimTypes.Username] = "root",
            [PanelClaimTypes.SessionId] = OperatorSessionId.ToString(),
        });
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Username, "acme"),
            (PanelClaimTypes.Role, nameof(UserRole.Customer)),
            (PanelClaimTypes.AccountId, AccountId.ToString()),
            (PanelClaimTypes.SessionKind, "ViewAs"),
            (PanelClaimTypes.Actor, actor));

        var customer = Assert.IsType<CustomerCaller>(user.Caller);
        Assert.Equal(UserId, customer.UserId);
        Assert.Equal(new ViewingOperator(OperatorId, "root"), customer.ViewedBy);
        Assert.Equal(OperatorId, user.ActorUserId);
        Assert.Equal("root", user.ActorUsername);
    }

    /// <summary>A view-as marker with no readable operator is refused, not read as an own sign-in.</summary>
    [Fact]
    public void A_view_as_marker_with_no_readable_operator_is_refused_not_read_as_an_own_sign_in()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, nameof(UserRole.Admin)),
            (PanelClaimTypes.SessionKind, "ViewAs"));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>A signed-in token carrying an unknown role is refused, not demoted to anonymous or customer.</summary>
    [Fact]
    public void A_signed_in_token_carrying_an_unknown_role_is_refused()
    {
        // An older token, or one forged with a role this build has never heard of. It verified, so
        // it reaches this class — and must be neither an administrator nor a quiet "nobody".
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, "SuperAdmin"),
            (PanelClaimTypes.AccountId, AccountId.ToString()));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>The role comparison is case sensitive.</summary>
    [Fact]
    public void The_role_comparison_is_case_sensitive()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, "admin"));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>A signed-in token with no role is refused.</summary>
    [Fact]
    public void A_signed_in_token_with_no_role_is_refused()
    {
        var user = CurrentUser((PanelClaimTypes.UserId, UserId.ToString()));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>A malformed user identifier is refused rather than read as an empty id.</summary>
    [Fact]
    public void A_malformed_user_identifier_is_refused_rather_than_read_as_an_empty_id()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, "not-a-guid"),
            (PanelClaimTypes.Role, nameof(UserRole.Admin)));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>A customer token with no account claim is a customer acting for none, not a refusal.</summary>
    [Fact]
    public void A_customer_token_with_no_account_is_a_customer_acting_for_none()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Username, "acme"),
            (PanelClaimTypes.Role, nameof(UserRole.Customer)));

        var customer = Assert.IsType<AccountlessCustomerCaller>(user.Caller);
        Assert.Equal(UserId, customer.UserId);
        Assert.Equal("acme", customer.Username);
        Assert.False(user.IsAdmin);
        Assert.Null(user.AccountId);
    }

    /// <summary>A customer token whose account claim is present but not an id is refused, not read as no account.</summary>
    [Fact]
    public void A_customer_token_with_a_malformed_account_is_refused()
    {
        var user = CurrentUser(
            (PanelClaimTypes.UserId, UserId.ToString()),
            (PanelClaimTypes.Role, nameof(UserRole.Customer)),
            (PanelClaimTypes.AccountId, "not-a-guid"));

        Assert.Throws<UnrecognisedPrincipalException>(() =>
        {
            return user.Caller;
        });
    }

    /// <summary>A request with no authenticated principal is anonymous, whatever claims it carries.</summary>
    [Fact]
    public void A_request_with_no_authenticated_principal_is_anonymous()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(PanelClaimTypes.Role, nameof(UserRole.Admin))])),
        };
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.Same(AnonymousCaller.Instance, user.Caller);
        Assert.False(user.IsAdmin);
        Assert.Equal(Guid.Empty, user.UserId);
        Assert.Null(user.AccountId);
    }

    /// <summary>With no HTTP context at all the caller is anonymous rather than a crash.</summary>
    [Fact]
    public void With_no_http_context_at_all_the_caller_is_anonymous_rather_than_a_crash()
    {
        // Background work resolving the scoped service outside a request: there is no context to
        // read, and the honest answer is a caller that owns nothing.
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = null });

        Assert.Same(AnonymousCaller.Instance, user.Caller);
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
