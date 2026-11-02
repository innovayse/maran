using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Tests.Callers;

/// <summary>
/// Behavioural contract of the members <see cref="ICurrentUser"/> still derives from its
/// <see cref="Caller"/>: every old answer, for each kind of caller, with its old meaning.
/// </summary>
public sealed class CurrentUserDerivationTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>An anonymous caller reads as nobody: empty id, empty name, no account, no actor.</summary>
    [Fact]
    public void An_anonymous_caller_reads_as_nobody()
    {
        ICurrentUser user = new Holder(AnonymousCaller.Instance);

        Assert.Equal(Guid.Empty, user.UserId);
        Assert.Equal(string.Empty, user.Username);
        Assert.Null(user.AccountId);
        Assert.False(user.IsAdmin);
        Assert.Null(user.ActorUserId);
        Assert.Equal(string.Empty, user.ActorUsername);
    }

    /// <summary>An administrator reads as an administrator with an id and name but no account.</summary>
    [Fact]
    public void An_administrator_reads_as_an_administrator_without_an_account()
    {
        ICurrentUser user = new Holder(new AdministratorCaller(UserId, "root"));

        Assert.Equal(UserId, user.UserId);
        Assert.Equal("root", user.Username);
        Assert.Null(user.AccountId);
        Assert.True(user.IsAdmin);
        Assert.Null(user.ActorUserId);
        Assert.Equal(string.Empty, user.ActorUsername);
    }

    /// <summary>A customer reads as a non-administrator that owns its account.</summary>
    [Fact]
    public void A_customer_reads_as_a_non_administrator_owning_its_account()
    {
        ICurrentUser user = new Holder(new CustomerCaller(UserId, "acme", AccountId));

        Assert.Equal(UserId, user.UserId);
        Assert.Equal("acme", user.Username);
        Assert.Equal(AccountId, user.AccountId);
        Assert.False(user.IsAdmin);
        Assert.Null(user.ActorUserId);
        Assert.Equal(string.Empty, user.ActorUsername);
    }

    /// <summary>A customer acting for no account keeps its identity but owns nothing and is not an administrator.</summary>
    [Fact]
    public void A_customer_acting_for_no_account_keeps_its_identity_but_owns_nothing()
    {
        ICurrentUser user = new Holder(new AccountlessCustomerCaller(UserId, "acme"));

        Assert.Equal(UserId, user.UserId);
        Assert.Equal("acme", user.Username);
        Assert.Null(user.AccountId);
        Assert.False(user.IsAdmin);
        Assert.Null(user.ActorUserId);
        Assert.Equal(string.Empty, user.ActorUsername);
    }

    /// <summary>An operator viewing as a customer is the actor while the customer stays the principal.</summary>
    [Fact]
    public void An_operator_viewing_as_a_customer_is_the_actor_and_the_customer_stays_the_principal()
    {
        ICurrentUser user = new Holder(
            new CustomerCaller(UserId, "acme", AccountId, new ViewingOperator(OperatorId, "root")));

        Assert.Equal(UserId, user.UserId);
        Assert.Equal("acme", user.Username);
        Assert.Equal(AccountId, user.AccountId);
        Assert.False(user.IsAdmin);
        Assert.Equal(OperatorId, user.ActorUserId);
        Assert.Equal("root", user.ActorUsername);
    }

    /// <summary>An operator viewing as an administrator is the actor while the administrator stays the principal.</summary>
    [Fact]
    public void An_operator_viewing_as_an_administrator_is_the_actor_and_the_administrator_stays_the_principal()
    {
        ICurrentUser user = new Holder(
            new AdministratorCaller(UserId, "root", new ViewingOperator(OperatorId, "other-root")));

        Assert.True(user.IsAdmin);
        Assert.Equal(UserId, user.UserId);
        Assert.Equal(OperatorId, user.ActorUserId);
        Assert.Equal("other-root", user.ActorUsername);
    }

    /// <summary>The kinds of caller are distinct values, and value equality tells equal ones apart.</summary>
    [Fact]
    public void The_kinds_of_caller_are_distinct_and_equal_callers_compare_equal()
    {
        Caller administrator = new AdministratorCaller(UserId, "root");
        Caller customer = new CustomerCaller(UserId, "root", AccountId);

        Assert.NotEqual(administrator, customer);
        Assert.NotEqual<Caller>(new AccountlessCustomerCaller(UserId, "root"), administrator);
        Assert.NotEqual<Caller>(AnonymousCaller.Instance, administrator);
        Assert.Equal(administrator, new AdministratorCaller(UserId, "root"));
        Assert.Same(AnonymousCaller.Instance, AnonymousCaller.Instance);
    }

    /// <summary>An <see cref="ICurrentUser"/> that supplies only <see cref="Caller"/>, as every implementer now does.</summary>
    private sealed class Holder : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; }

        /// <summary>Creates the holder.</summary>
        /// <param name="caller">The caller every derived answer is read from.</param>
        public Holder(Caller caller)
        {
            Caller = caller;
        }
    }
}
