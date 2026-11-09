using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// The PHP bounds an <see cref="AccountDirectory"/> snapshot carries (issue #138). The snapshot's
/// member has a default, so a projection that forgot to fill it compiles and answers the default for
/// every account on the host; each read path therefore needs its own assertion with figures unlike
/// the default.
/// </summary>
public sealed class AccountDirectoryPhpLimitsTests : IDisposable
{
    /// <summary>The module's context, in memory, for an administrator.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>The scoped read of one account carries its plan's bounds.</summary>
    [Fact]
    public async Task The_scoped_read_of_one_account_carries_its_plans_bounds()
    {
        var accountId = await SeedAsync("acme", PhpLimits.Of(128, 32, 60, 1_000));

        var snapshot = await new AccountDirectory(_dbContext, FakeCurrentUser.Admin())
            .FindAsync(accountId, CancellationToken.None);

        Assert.Equal(new PhpLimitsSnapshot(128, 32, 60, 1_000), snapshot!.PhpLimits);
    }

    /// <summary>The host-wide list carries each account's own plan's bounds.</summary>
    [Fact]
    public async Task The_host_wide_list_carries_each_accounts_own_plans_bounds()
    {
        var small = await SeedAsync("small", PhpLimits.Of(128, 32, 60, 1_000));
        var large = await SeedAsync("large", PhpLimits.Of(512, 128, 300, 10_000));

        var snapshots = await new AccountDirectory(_dbContext, FakeCurrentUser.Admin())
            .ListAsync(CancellationToken.None);

        Assert.Equal(
            new PhpLimitsSnapshot(128, 32, 60, 1_000),
            Assert.Single(snapshots, snapshot =>
            {
                return snapshot.Id == small;
            }).PhpLimits);
        Assert.Equal(
            new PhpLimitsSnapshot(512, 128, 300, 10_000),
            Assert.Single(snapshots, snapshot =>
            {
                return snapshot.Id == large;
            }).PhpLimits);
    }

    /// <summary>An account on a plan that never stated bounds reads the Sdk's default.</summary>
    [Fact]
    public async Task An_account_on_a_plan_that_never_stated_bounds_reads_the_sdks_default()
    {
        var accountId = await SeedAsync("plain", null);

        var snapshot = await new AccountDirectory(_dbContext, FakeCurrentUser.Admin())
            .FindAsync(accountId, CancellationToken.None);

        Assert.Equal(PhpLimitsSnapshot.Default, snapshot!.PhpLimits);
    }

    /// <summary>Seeds one plan with these bounds (or the default) and one account on it.</summary>
    /// <param name="username">The account's system user name; the plan is named after it.</param>
    /// <param name="limits">The plan's bounds, or <c>null</c> to leave the default.</param>
    /// <returns>The account's id.</returns>
    private async Task<Guid> SeedAsync(string username, PhpLimits? limits)
    {
        var plan = Plan.Define(Guid.NewGuid(), $"Plan {username}", 5_120, 7, 2, 3, 17, 13, 11);
        if (limits is not null)
        {
            plan.ChangePhpLimits(limits);
        }

        var account = new Account(
            Guid.NewGuid(),
            username,
            $"{username}.example.com",
            plan.Id,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync();

        return account.Id;
    }
}
