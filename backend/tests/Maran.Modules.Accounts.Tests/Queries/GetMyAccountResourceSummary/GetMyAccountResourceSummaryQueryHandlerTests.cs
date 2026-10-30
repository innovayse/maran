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

    /// <summary>The in-memory Accounts context the handler reads.</summary>
    private readonly AccountsDbContext _context = AccountsTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A customer is summarised on the account their token names.</summary>
    [Fact]
    public async Task A_customer_is_summarised_on_the_account_their_token_names()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", plan.Id, Now, "owner@example.com");
        _context.Plans.Add(plan);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 2));

        var result = await new GetMyAccountResourceSummaryQueryHandler(
                FakeCurrentUser.Customer(account.Id), new AccountResourceSummaryReader(_context, [sites]))
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

        var result = await new GetMyAccountResourceSummaryQueryHandler(
                FakeCurrentUser.Admin(), new AccountResourceSummaryReader(_context, [sites]))
            .HandleAsync(new GetMyAccountResourceSummaryQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(sites.Asked);
    }
}
