using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of <see cref="SignedInCallerPolicy"/>: the signed-in principal, or a refusal.</summary>
public sealed class SignedInCallerPolicyTests
{
    /// <summary>An administrator is handed back as the principal it is.</summary>
    [Fact]
    public void An_administrator_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = SignedInCallerPolicy.Require(new StubCurrentUser(id, "root"));

        Assert.IsType<AdministratorCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>A customer acting for an account is handed back as the principal it is.</summary>
    [Fact]
    public void A_customer_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = SignedInCallerPolicy.Require(new StubCustomerUser(id, "acme", Guid.NewGuid()));

        Assert.IsType<CustomerCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>A customer acting for no account is still a principal.</summary>
    [Fact]
    public void A_customer_acting_for_no_account_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = SignedInCallerPolicy.Require(new StubCustomerUser(id, "acme", null));

        Assert.IsType<AccountlessCustomerCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>A reseller is handed back as the principal it is, carrying its reseller id.</summary>
    [Fact]
    public void A_reseller_is_the_principal()
    {
        var id = Guid.NewGuid();
        var resellerId = Guid.NewGuid();

        var principal = SignedInCallerPolicy.Require(new StubResellerUser(id, "northwind", resellerId));

        var reseller = Assert.IsType<ResellerCaller>(principal);
        Assert.Equal(id, reseller.UserId);
        Assert.Equal(resellerId, reseller.ResellerId);
        Assert.Null(reseller.ViewedBy);
    }

    /// <summary>The operator viewing as a reseller's login travels with the reseller principal.</summary>
    [Fact]
    public void The_operator_viewing_as_a_reseller_is_kept()
    {
        var operatorId = Guid.NewGuid();
        var viewing = ViewingAsCurrentUser.ForReseller(Guid.NewGuid(), "northwind", Guid.NewGuid(), operatorId, "root");

        var principal = SignedInCallerPolicy.Require(viewing);

        Assert.IsType<ResellerCaller>(principal);
        Assert.Equal(operatorId, principal.ViewedBy!.UserId);
        Assert.Equal("root", principal.ViewedBy.Username);
    }

    /// <summary>The operator viewing as a login travels with the principal.</summary>
    [Fact]
    public void The_operator_viewing_as_a_login_is_kept()
    {
        var operatorId = Guid.NewGuid();

        var viewing = new ViewingAsCurrentUser(Guid.NewGuid(), "acme", operatorId, "root");

        var principal = SignedInCallerPolicy.Require(viewing);

        Assert.Equal(operatorId, principal.ViewedBy!.UserId);
    }

    /// <summary>An API key is refused: it is not a login, and has no user id to act as.</summary>
    [Fact]
    public void An_api_key_caller_is_refused()
    {
        var apiKey = new ApiKeyCurrentUser();

        Assert.Throws<InvalidOperationException>(() =>
        {
            SignedInCallerPolicy.Require(apiKey);
        });
    }

    /// <summary>Nobody signed in is refused, never treated as a user with an empty id.</summary>
    [Fact]
    public void An_anonymous_caller_is_refused()
    {
        var anonymous = new AnonymousCurrentUser();

        Assert.Throws<InvalidOperationException>(() =>
        {
            SignedInCallerPolicy.Require(anonymous);
        });
    }

    /// <summary>An <see cref="ICurrentUser"/> double for a request authenticated by an API key.</summary>
    private sealed class ApiKeyCurrentUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; } = new ApiKeyCaller(Guid.NewGuid(), ["self.read"]);
    }

    /// <summary>An <see cref="ICurrentUser"/> double for a request with nobody signed in.</summary>
    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; } = AnonymousCaller.Instance;
    }
}
