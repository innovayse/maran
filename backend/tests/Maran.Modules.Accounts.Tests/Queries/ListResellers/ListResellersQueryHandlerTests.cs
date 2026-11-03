using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListResellers;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.ListResellers;

/// <summary>
/// Behavioral contract of <see cref="ListResellersQueryHandler"/>, run against a real
/// <see cref="AccountsDbContext"/> on the EF Core InMemory provider.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; the search is covered against a real server by the integration
/// suite.
/// </remarks>
public sealed class ListResellersQueryHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context.</summary>
    /// <param name="query">The list request.</param>
    private Task<Result<PagedResult<ResellerDto>>> RunAsync(ListResellersQuery query)
    {
        var names = new StubDisplayNames()
            .With("ResellerStatusActive", "Live")
            .With("ResellerStatusSuspended", "Paused");
        return new ListResellersQueryHandler(_dbContext, new ResellerUsageReader(_dbContext), names)
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>Stores resellers.</summary>
    /// <param name="resellers">The resellers to store.</param>
    private async Task StoreAsync(params Reseller[] resellers)
    {
        _dbContext.Resellers.AddRange(resellers);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>The names of a page's rows, in order.</summary>
    /// <param name="result">The page.</param>
    private static IEnumerable<string> Names(Result<PagedResult<ResellerDto>> result)
    {
        return result.Value.Items.Select(dto => { return dto.Name; });
    }

    /// <summary>An empty store answers an empty page with a total of zero.</summary>
    [Fact]
    public async Task An_empty_store_answers_an_empty_page_with_a_total_of_zero()
    {
        var result = await RunAsync(new ListResellersQuery());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>A reseller is mapped onto its row with the localized state name.</summary>
    [Fact]
    public async Task A_reseller_is_mapped_onto_its_row_with_the_localized_state_name()
    {
        var reseller = ResellerTestData.Reseller("acme");
        reseller.Suspend();
        await StoreAsync(reseller);

        var result = await RunAsync(new ListResellersQuery());

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal(reseller.Id, dto.Id);
        Assert.Equal("acme", dto.Name);
        Assert.Equal("acme@example.com", dto.ContactEmail);
        Assert.Equal(ResellerStatus.Suspended, dto.Status);
        Assert.Equal("Paused", dto.StatusName);
        Assert.Equal(ResellerTestData.CreatedAt, dto.CreatedAt);
    }

    /// <summary>With no sort named, resellers come by name, first to last.</summary>
    [Fact]
    public async Task With_no_sort_named_resellers_come_by_name_first_to_last()
    {
        await StoreAsync(
            ResellerTestData.Reseller("charlie"),
            ResellerTestData.Reseller("alpha"),
            ResellerTestData.Reseller("bravo"));

        var result = await RunAsync(new ListResellersQuery());

        Assert.Equal(["alpha", "bravo", "charlie"], Names(result));
    }

    /// <summary>A descending name sort orders the page last name first.</summary>
    [Fact]
    public async Task A_descending_name_sort_orders_the_page_last_name_first()
    {
        await StoreAsync(
            ResellerTestData.Reseller("charlie"),
            ResellerTestData.Reseller("alpha"),
            ResellerTestData.Reseller("bravo"));

        var result = await RunAsync(new ListResellersQuery(Sort: SortRequest.Parse("-name")));

        Assert.Equal(["charlie", "bravo", "alpha"], Names(result));
    }

    /// <summary>A creation time sort and a state sort each order the page by their own field.</summary>
    [Fact]
    public async Task A_creation_time_sort_and_a_state_sort_each_order_by_their_own_field()
    {
        var oldest = new Reseller(
            Guid.NewGuid(),
            "zulu",
            "z@example.com",
            "en",
            ResellerTestData.Envelope(),
            ResellerTestData.CreatedAt);
        var newest = new Reseller(
            Guid.NewGuid(),
            "alpha",
            "a@example.com",
            "en",
            ResellerTestData.Envelope(),
            ResellerTestData.CreatedAt.AddDays(5));
        newest.Suspend();
        await StoreAsync(newest, oldest);

        var byCreation = await RunAsync(new ListResellersQuery(Sort: SortRequest.Parse("createdAt")));
        var byStatusDescending = await RunAsync(new ListResellersQuery(Sort: SortRequest.Parse("-status")));

        Assert.Equal(["zulu", "alpha"], Names(byCreation));
        Assert.Equal(["alpha", "zulu"], Names(byStatusDescending));
    }

    /// <summary>The second page holds the rows after the first and the total counts every reseller.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_reseller()
    {
        await StoreAsync(Enumerable.Range(0, 12).Select(index =>
        {
            return ResellerTestData.Reseller($"reseller{index:D2}");
        }).ToArray());

        var result = await RunAsync(new ListResellersQuery(Page: 2, PageSize: 10));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["reseller10", "reseller11"], Names(result));
    }

    /// <summary>The status filter keeps only resellers in that state, and the total counts only them.</summary>
    [Fact]
    public async Task The_status_filter_keeps_only_resellers_in_that_state()
    {
        var paused = ResellerTestData.Reseller("paused");
        paused.Suspend();
        await StoreAsync(paused, ResellerTestData.Reseller("running"));

        var suspended = await RunAsync(new ListResellersQuery(Status: ResellerStatus.Suspended));
        var active = await RunAsync(new ListResellersQuery(Status: ResellerStatus.Active));

        Assert.Equal(["paused"], Names(suspended));
        Assert.Equal(1, suspended.Value.Total);
        Assert.Equal(["running"], Names(active));
    }

    /// <summary>A row carries the account count, plan count and the disk the accounts are promised.</summary>
    [Fact]
    public async Task A_row_carries_the_account_count_plan_count_and_disk_the_accounts_are_promised()
    {
        var acme = ResellerTestData.Reseller("acme");
        var idle = ResellerTestData.Reseller("idle");
        var big = Plan.Define(Guid.NewGuid(), "Big", 3_000, 1, 1, 1, 0, 0, 1, resellerId: acme.Id);
        var small = Plan.Define(Guid.NewGuid(), "Small", 1_000, 1, 1, 1, 0, 0, 1, resellerId: acme.Id);
        await StoreAsync(acme, idle);
        _dbContext.Plans.AddRange(big, small);
        _dbContext.Accounts.AddRange(
            OwnedAccount("one", big, acme),
            OwnedAccount("two", big, acme),
            OwnedAccount("three", small, acme));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync(new ListResellersQuery());

        var acmeRow = result.Value.Items.Single(dto => { return dto.Name == "acme"; });
        Assert.Equal(3, acmeRow.AccountCount);
        Assert.Equal(2, acmeRow.PlanCount);
        Assert.Equal(7_000, acmeRow.DiskAllocatedMb);
        var idleRow = result.Value.Items.Single(dto => { return dto.Name == "idle"; });
        Assert.Equal(0, idleRow.AccountCount);
        Assert.Equal(0, idleRow.PlanCount);
        Assert.Equal(0, idleRow.DiskAllocatedMb);
    }

    /// <summary>Builds an account of <paramref name="owner"/> on <paramref name="plan"/>.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="plan">The plan the account is on.</param>
    /// <param name="owner">The reseller that owns the account.</param>
    private static Account OwnedAccount(string name, Plan plan, Reseller owner)
    {
        return new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: owner.Id);
    }
}
