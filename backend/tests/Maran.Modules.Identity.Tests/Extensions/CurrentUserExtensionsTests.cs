using Maran.Modules.Identity.Extensions;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.Extensions;

/// <summary>Behavioural contract of <see cref="CurrentUserExtensions"/>: the signed-in principal, or a refusal.</summary>
public sealed class CurrentUserExtensionsTests
{
    /// <summary>An administrator is handed back as the principal it is.</summary>
    [Fact]
    public void An_administrator_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = new StubCurrentUser(id, "root").RequirePrincipal();

        Assert.IsType<AdministratorCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>A customer acting for an account is handed back as the principal it is.</summary>
    [Fact]
    public void A_customer_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = new StubCustomerUser(id, "acme", Guid.NewGuid()).RequirePrincipal();

        Assert.IsType<CustomerCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>A customer acting for no account is still a principal.</summary>
    [Fact]
    public void A_customer_acting_for_no_account_is_the_principal()
    {
        var id = Guid.NewGuid();

        var principal = new StubCustomerUser(id, "acme", null).RequirePrincipal();

        Assert.IsType<AccountlessCustomerCaller>(principal);
        Assert.Equal(id, principal.UserId);
    }

    /// <summary>The operator viewing as a login travels with the principal.</summary>
    [Fact]
    public void The_operator_viewing_as_a_login_is_kept()
    {
        var operatorId = Guid.NewGuid();

        var principal = new ViewingAsCurrentUser(Guid.NewGuid(), "acme", operatorId, "root").RequirePrincipal();

        Assert.Equal(operatorId, principal.ViewedBy!.UserId);
    }

    /// <summary>Nobody signed in is refused, never treated as a user with an empty id.</summary>
    [Fact]
    public void An_anonymous_caller_is_refused()
    {
        var anonymous = new AnonymousCurrentUser();

        Assert.Throws<InvalidOperationException>(() => anonymous.RequirePrincipal());
    }

    /// <summary>An <see cref="ICurrentUser"/> double for a request nobody signed in to.</summary>
    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; } = AnonymousCaller.Instance;
    }
}
