using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetAccountResourceSummary;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetAccountResourceSummary;

/// <summary>
/// Behavioural contract of the administrator's resource summary: each module's count paired with the
/// plan's limit, every state stated rather than implied.
/// </summary>
public sealed class GetAccountResourceSummaryQueryHandlerTests : IDisposable
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

    /// <summary>A missing account answers not found.</summary>
    [Fact]
    public async Task A_missing_account_answers_not_found()
    {
        var result = await Handler().HandleAsync(
            new GetAccountResourceSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Each count is paired with the plan limit for its kind.</summary>
    [Fact]
    public async Task Each_count_is_paired_with_the_plan_limit_for_its_kind()
    {
        var accountId = await SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 7));
        var databases = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Databases, 1));

        var result = await Handler(databases, sites).HandleAsync(
            new GetAccountResourceSummaryQuery(accountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(accountId, result.Value.AccountId);
        Assert.Collection(
            result.Value.Resources,
            row =>
            {
                Assert.Equal(AccountResourceKind.Sites, row.Kind);
                Assert.Equal(AccountResourceCountState.Counted, row.State);
                Assert.Equal(7, row.Used);
                Assert.Equal(ResourceLimitKind.Limited, row.LimitKind);
                Assert.Equal(10, row.Limit);
            },
            row =>
            {
                Assert.Equal(AccountResourceKind.Databases, row.Kind);
                Assert.Equal(1, row.Used);
                Assert.Equal(5, row.Limit);
            });
        Assert.Equal([accountId], sites.Asked);
    }

    /// <summary>Backups come through with no plan limit.</summary>
    [Fact]
    public async Task Backups_come_through_with_no_plan_limit()
    {
        var accountId = await SeedAsync();
        var backups = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Backups, 4));

        var result = await Handler(backups).HandleAsync(
            new GetAccountResourceSummaryQuery(accountId), CancellationToken.None);

        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(4, row.Used);
        Assert.Equal(ResourceLimitKind.Unlimited, row.LimitKind);
    }

    /// <summary>A kind with no registered counter is left out rather than reported as zero.</summary>
    [Fact]
    public async Task A_kind_with_no_registered_counter_is_left_out_rather_than_reported_as_zero()
    {
        var accountId = await SeedAsync();

        var result = await Handler().HandleAsync(
            new GetAccountResourceSummaryQuery(accountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Resources);
    }

    /// <summary>An unavailable count comes through as unavailable, beside its limit.</summary>
    [Fact]
    public async Task An_unavailable_count_comes_through_as_unavailable_beside_its_limit()
    {
        var accountId = await SeedAsync();
        var cron = new StubAccountResourceCounter(AccountResourceCount.Unavailable(AccountResourceKind.CronEntries));

        var result = await Handler(cron).HandleAsync(
            new GetAccountResourceSummaryQuery(accountId), CancellationToken.None);

        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(AccountResourceCountState.Unavailable, row.State);
        Assert.Equal(ResourceLimitKind.Limited, row.LimitKind);
        Assert.Equal(3, row.Limit);
    }

    /// <summary>Seeds one account on a plan selling 10 sites, 5 databases, 4 SFTP, 2 FTPS and 3 cron entries.</summary>
    /// <returns>The account's id.</returns>
    private async Task<Guid> SeedAsync()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", plan.Id, Now, "owner@example.com");
        _context.Plans.Add(plan);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>Builds the handler over the fixture's context and the given counters.</summary>
    /// <param name="counters">The counters registered for this test.</param>
    /// <returns>The handler under test.</returns>
    private GetAccountResourceSummaryQueryHandler Handler(params IAccountResourceCounter[] counters)
    {
        return new GetAccountResourceSummaryQueryHandler(new AccountResourceSummaryReader(_context, counters));
    }
}
