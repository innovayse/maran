using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Which callers the accounts search answers for at all. UNOBSERVED HERE: the match and the tenant
/// scoping through it use <c>ILIKE</c>, which the in-memory provider cannot evaluate; the integration
/// suite holds those.
/// </summary>
public sealed class AccountsSearchProviderTests
{
    /// <summary>A customer, an API key, an accountless customer and an anonymous caller are offered no account, even their own by exact name.</summary>
    /// <param name="who">Which caller.</param>
    [Theory]
    [InlineData("customer")]
    [InlineData("apikey")]
    [InlineData("accountless")]
    [InlineData("anonymous")]
    public async Task Callers_without_the_accounts_screens_get_no_hits(string who)
    {
        var plan = new Plan(Guid.NewGuid(), "StarterPlan", 1024, 5, 5, 5, 5, 2);
        var account = new Account(Guid.NewGuid(), "mine", "mine.example.com", plan.Id, DateTimeOffset.UnixEpoch, "o@example.com");
        var caller = who switch
        {
            "customer" => FakeCurrentUser.Customer(account.Id),
            "apikey" => FakeCurrentUser.ApiKey(null),
            "accountless" => FakeCurrentUser.AccountlessCustomer(),
            _ => FakeCurrentUser.Anonymous(),
        };
        await using var context = AccountsTestContext.Create(caller);

        var hits = await new AccountsSearchProvider(context, caller).SearchAsync(new SearchQuery("mine"), CancellationToken.None);

        Assert.Empty(hits);
    }
}
