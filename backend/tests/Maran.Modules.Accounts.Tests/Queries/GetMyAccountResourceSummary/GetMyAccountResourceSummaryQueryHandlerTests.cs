using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetMyAccountResourceSummary;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetMyAccountResourceSummary;

/// <summary>
/// Behavioural contract of the customer's own resource summary: the account comes from the caller's
/// token, and a caller owning none gets not found.
/// </summary>
public sealed class GetMyAccountResourceSummaryQueryHandlerTests : IDisposable
{
    /// <summary>When the seeded account was created.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory database both contexts of a test open.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>The administrator's context, which writes the fixtures and is never narrowed.</summary>
    private readonly AccountsDbContext _seed;

    /// <summary>The contexts opened for the caller under test, disposed with the fixture.</summary>
    private readonly List<AccountsDbContext> _opened = [];

    /// <summary>Opens the administrator's context over the test's database.</summary>
    public GetMyAccountResourceSummaryQueryHandlerTests()
    {
        _seed = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _seed.Dispose();
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    /// <summary>A customer is summarised on the account their token names.</summary>
    /// <remarks>
    /// The rows are written as an administrator and read through a context filtered for the customer,
    /// as in production, so the account and the plan it names must both pass the customer's filters:
    /// a filter that hid either would turn this answer into <c>AccountNotFound</c>.
    /// </remarks>
    [Fact]
    public async Task A_customer_is_summarised_on_the_account_their_token_names()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", plan.Id, Now, "owner@example.com");
        _seed.Plans.Add(plan);
        _seed.Accounts.Add(account);
        await _seed.SaveChangesAsync();
        var customer = FakeCurrentUser.Customer(account.Id);
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 2));

        var result = await new GetMyAccountResourceSummaryQueryHandler(customer, ReaderOf(customer, sites))
            .HandleAsync(new GetMyAccountResourceSummaryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Id, result.Value.AccountId);
        Assert.Equal([account.Id], sites.Asked);
    }

    /// <summary>An administrator owns no account and gets not found without any counter being asked.</summary>
    [Fact]
    public async Task An_administrator_owns_no_account_and_gets_not_found_without_any_counter_being_asked()
    {
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 2));

        var admin = FakeCurrentUser.Admin();

        var result = await new GetMyAccountResourceSummaryQueryHandler(admin, ReaderOf(admin, sites))
            .HandleAsync(new GetMyAccountResourceSummaryQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(sites.Asked);
    }

    /// <summary>Builds the shared reader over a context filtered for the caller, and one counter.</summary>
    /// <param name="caller">The caller the context and the reseller scope are built for.</param>
    /// <param name="counter">The counter registered for this test.</param>
    /// <returns>The reader.</returns>
    private AccountResourceSummaryReader ReaderOf(FakeCurrentUser caller, StubAccountResourceCounter counter)
    {
        var context = AccountsTestContext.CreateOn(_database, caller);
        _opened.Add(context);
        return new AccountResourceSummaryReader(context, new ResellerScope(context, caller), [counter]);
    }
}
