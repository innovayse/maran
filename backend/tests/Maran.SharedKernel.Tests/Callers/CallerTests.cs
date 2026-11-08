using Maran.SharedKernel.Callers;

namespace Maran.SharedKernel.Tests.Callers;

/// <summary>
/// Behavioural contract of the <see cref="Caller"/> cases as values: which are distinct from which,
/// which compare equal, that a customer acting for no account is a caller of its own, and that a
/// reseller names the reseller it acts for.
/// </summary>
public sealed class CallerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ResellerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
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

    /// <summary>A reseller names the reseller it acts for, and is neither an administrator nor a customer.</summary>
    [Fact]
    public void A_reseller_names_the_reseller_it_acts_for_and_is_neither_an_administrator_nor_a_customer()
    {
        Caller caller = new ResellerCaller(UserId, "agency", ResellerId);

        var reseller = Assert.IsType<ResellerCaller>(caller);
        Assert.IsAssignableFrom<PrincipalCaller>(caller);
        Assert.Equal(UserId, reseller.UserId);
        Assert.Equal("agency", reseller.Username);
        Assert.Equal(ResellerId, reseller.ResellerId);
        Assert.Null(reseller.ViewedBy);
        Assert.NotEqual<Caller>(reseller, new AdministratorCaller(UserId, "agency"));
        Assert.NotEqual<Caller>(reseller, new CustomerCaller(UserId, "agency", ResellerId));
        Assert.NotEqual<Caller>(reseller, new AccountlessCustomerCaller(UserId, "agency"));
        Assert.NotEqual(reseller, new ResellerCaller(UserId, "agency", Guid.NewGuid()));
        Assert.Equal(reseller, new ResellerCaller(UserId, "agency", ResellerId));
    }

    /// <summary>An operator viewing as a reseller is recorded beside it and makes it another caller.</summary>
    [Fact]
    public void An_operator_viewing_as_a_reseller_is_recorded_beside_it_and_changes_the_caller()
    {
        var viewed = new ResellerCaller(UserId, "agency", ResellerId, new ViewingOperator(OperatorId, "root"));
        var alone = new ResellerCaller(UserId, "agency", ResellerId);

        Assert.Equal(new ViewingOperator(OperatorId, "root"), viewed.ViewedBy);
        Assert.Equal(ResellerId, viewed.ResellerId);
        Assert.Null(alone.ViewedBy);
        Assert.NotEqual(alone, viewed);
    }

    /// <summary>An API key caller names its key and scopes, is no login, and is no other kind of caller.</summary>
    [Fact]
    public void An_api_key_caller_names_its_key_and_scopes_and_is_no_login()
    {
        var keyId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        Caller caller = new ApiKeyCaller(keyId, ["self.read"]);

        var apiKey = Assert.IsType<ApiKeyCaller>(caller);
        Assert.IsNotAssignableFrom<PrincipalCaller>(caller);
        Assert.Equal(keyId, apiKey.KeyId);
        Assert.Equal(["self.read"], apiKey.Scopes);
        Assert.Equal("api-key:66666666666666666666666666666666", apiKey.ActorName);
        Assert.NotEqual<Caller>(apiKey, AnonymousCaller.Instance);
        Assert.NotEqual<Caller>(apiKey, new AdministratorCaller(keyId, "root"));
    }

    /// <summary>An API key caller carries the reseller that owns it when one does, and none otherwise; it is never a reseller login.</summary>
    [Fact]
    public void An_api_key_caller_carries_its_owning_reseller_only_when_a_reseller_owns_it()
    {
        var resellerId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var owned = new ApiKeyCaller(Guid.NewGuid(), ["plans.read"], resellerId);
        var administrators = new ApiKeyCaller(Guid.NewGuid(), ["server.read"]);

        Assert.Equal(resellerId, owned.ResellerId);
        Assert.Null(administrators.ResellerId);
        Assert.IsNotAssignableFrom<ResellerCaller>(owned);
        Assert.IsNotAssignableFrom<PrincipalCaller>(owned);
    }
}
