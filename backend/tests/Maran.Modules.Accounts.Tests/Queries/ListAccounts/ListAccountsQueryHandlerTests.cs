using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccounts;

/// <summary>
/// Behavioral contract of <see cref="ListAccountsQueryHandler"/>, run against a real
/// <see cref="AccountsDbContext"/> backed by the EF Core InMemory provider, each test getting its
/// own uniquely-named database (rules/testing.md "Determinism").
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>AccountListEndpointTests</c> covers it against a real server.
/// </remarks>
public sealed class ListAccountsQueryHandlerTests
{
    /// <summary>The instant every account in these tests is created at unless a test says otherwise.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>An empty store answers an empty page with a total of zero.</summary>
    [Fact]
    public async Task An_empty_store_answers_an_empty_page_with_a_total_of_zero()
    {
        await using var dbContext = CreateDbContext();

        var result = await HandleAsync(dbContext, new ListAccountsQuery());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>An account is mapped field for field onto its dto.</summary>
    [Fact]
    public async Task An_account_is_mapped_field_for_field_onto_its_dto()
    {
        await using var dbContext = CreateDbContext();
        var planId = Guid.NewGuid();
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", planId, Epoch);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery());

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal(account.Id, dto.Id);
        Assert.Equal("acme", dto.Name);
        Assert.Equal("acme.example.com", dto.PrimaryDomain);
        Assert.Equal(planId, dto.PlanId);
        Assert.Equal(AccountStatus.Active, dto.Status);
        Assert.Equal(Epoch, dto.CreatedAt);
    }

    /// <summary>With no sort named, accounts come oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_accounts_come_oldest_first()
    {
        await using var dbContext = CreateDbContext();
        // Inserted out of chronological order so the assertion cannot pass by insertion-order accident.
        dbContext.Accounts.AddRange(
            NewAccount("third", Epoch.AddDays(2)),
            NewAccount("first", Epoch),
            NewAccount("second", Epoch.AddDays(1)));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery());

        Assert.Equal(["first", "second", "third"], Names(result.Value.Items));
    }

    /// <summary>A descending name sort orders the page by name, last first.</summary>
    [Fact]
    public async Task A_descending_name_sort_orders_the_page_by_name_last_first()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Accounts.AddRange(NewAccount("bravo", Epoch), NewAccount("charlie", Epoch), NewAccount("alpha", Epoch));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery(Sort: SortRequest.Parse("-name")));

        Assert.Equal(["charlie", "bravo", "alpha"], Names(result.Value.Items));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every match.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_match()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Accounts.AddRange(Enumerable.Range(0, 12).Select(day =>
        {
            return NewAccount($"acct{day:D2}", Epoch.AddDays(day));
        }));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery(Page: 2, PageSize: 10));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(["acct10", "acct11"], Names(result.Value.Items));
    }

    /// <summary>A page past the last one answers empty with the true total and the page that was asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_one_answers_empty_with_the_true_total_and_the_page_asked_for()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Accounts.AddRange(NewAccount("alpha", Epoch), NewAccount("bravo", Epoch));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery(Page: 3, PageSize: 10));

        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(3, result.Value.Page);
    }

    /// <summary>Accounts sharing a creation instant are ordered by id, so a page boundary never splits them unstably.</summary>
    [Fact]
    public async Task Accounts_sharing_a_creation_instant_are_ordered_by_id()
    {
        await using var dbContext = CreateDbContext();
        var accounts = Enumerable.Range(0, 5).Select(index =>
        {
            return NewAccount($"tie{index}", Epoch);
        }).ToList();
        dbContext.Accounts.AddRange(accounts);
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery());

        Assert.Equal(accounts.Select(account => { return account.Id; }).Order(), result.Value.Items.Select(dto =>
        {
            return dto.Id;
        }));
    }

    /// <summary>The plan filter keeps only that plan's accounts, and the total counts only them.</summary>
    [Fact]
    public async Task The_plan_filter_keeps_only_that_plans_accounts()
    {
        await using var dbContext = CreateDbContext();
        var planId = Guid.NewGuid();
        dbContext.Accounts.AddRange(
            new Account(Guid.NewGuid(), "onplan", "onplan.example.com", planId, Epoch),
            NewAccount("elsewhere", Epoch));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery(PlanId: planId));

        Assert.Equal(1, result.Value.Total);
        Assert.Equal(["onplan"], Names(result.Value.Items));
    }

    /// <summary>The status filter keeps only accounts in that state.</summary>
    [Fact]
    public async Task The_status_filter_keeps_only_accounts_in_that_state()
    {
        await using var dbContext = CreateDbContext();
        var suspended = NewAccount("paused", Epoch);
        suspended.Suspend();
        dbContext.Accounts.AddRange(suspended, NewAccount("running", Epoch));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListAccountsQuery(Status: AccountStatus.Suspended));

        Assert.Equal(["paused"], Names(result.Value.Items));
    }

    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Builds an account on a plan of its own.</summary>
    /// <param name="name">The account's name; its domain is derived from it.</param>
    /// <param name="createdAt">When it was created.</param>
    private static Account NewAccount(string name, DateTimeOffset createdAt)
    {
        return new Account(Guid.NewGuid(), name, $"{name}.example.com", Guid.NewGuid(), createdAt);
    }

    /// <summary>Runs the handler under test.</summary>
    /// <param name="dbContext">The store to read.</param>
    /// <param name="query">The list request.</param>
    private static Task<Result<PagedResult<AccountDto>>> HandleAsync(
        AccountsDbContext dbContext,
        ListAccountsQuery query)
    {
        return new ListAccountsQueryHandler(dbContext).HandleAsync(query, CancellationToken.None);
    }

    /// <summary>The names of a page's rows, in order.</summary>
    /// <param name="items">The page's rows.</param>
    private static IEnumerable<string> Names(IEnumerable<AccountDto> items)
    {
        return items.Select(dto => { return dto.Name; });
    }
}
