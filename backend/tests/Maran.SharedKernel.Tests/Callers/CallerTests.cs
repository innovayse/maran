using Maran.SharedKernel.Callers;

namespace Maran.SharedKernel.Tests.Callers;

/// <summary>
/// Behavioural contract of the <see cref="Caller"/> cases as values: which are distinct from which,
/// which compare equal, and that a customer acting for no account is a caller of its own.
/// </summary>
public sealed class CallerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>The kinds of caller are distinct values, and equal callers compare equal.</summary>
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

    /// <summary>A customer acting for no account is a case of its own, not anonymous or any other.</summary>
    [Fact]
    public void A_customer_acting_for_no_account_is_a_caller_of_its_own()
    {
        Caller caller = new AccountlessCustomerCaller(UserId, "acme");

        var accountless = Assert.IsType<AccountlessCustomerCaller>(caller);
        Assert.IsAssignableFrom<PrincipalCaller>(caller);
        Assert.Equal(UserId, accountless.UserId);
        Assert.Equal("acme", accountless.Username);
        Assert.Null(accountless.ViewedBy);
        Assert.NotEqual<Caller>(accountless, new CustomerCaller(UserId, "acme", AccountId));
    }

    /// <summary>A customer names the account it acts for, and another account is another caller.</summary>
    [Fact]
    public void A_customer_names_the_account_it_acts_for_and_another_account_is_another_caller()
    {
        var customer = new CustomerCaller(UserId, "acme", AccountId);

        Assert.Equal(AccountId, customer.AccountId);
        Assert.NotEqual(customer, new CustomerCaller(UserId, "acme", Guid.NewGuid()));
    }

    /// <summary>An operator viewing as a login is recorded beside it and makes it another caller.</summary>
    [Fact]
    public void An_operator_viewing_as_a_login_is_recorded_beside_it_and_changes_the_caller()
    {
        var viewed = new CustomerCaller(UserId, "acme", AccountId, new ViewingOperator(OperatorId, "root"));
        var alone = new CustomerCaller(UserId, "acme", AccountId);

        Assert.Equal(new ViewingOperator(OperatorId, "root"), viewed.ViewedBy);
        Assert.Equal(UserId, viewed.UserId);
        Assert.Null(alone.ViewedBy);
        Assert.NotEqual(alone, viewed);
    }
}
