using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Persistence;

/// <summary>
/// The fail-closed query filters of <see cref="AccountsDbContext"/>: every kind of caller has exactly
/// one answer to "which accounts, plans and resellers do I see". A caller with no principal and a
/// customer acting for no account see nothing, a customer sees only its own account and that account's
/// plan, a reseller sees only what it owns, and only an administrator sees everything. Every refusing
/// control has its accepting control beside it, so a filter that hid everything, or nothing, would fail
/// one of them.
/// </summary>
public sealed class AccountsDbContextFailClosedTests
{
    /// <summary>The identity of the administrator-owned account, fixed so a lookup by key can be tried.</summary>
    private static readonly Guid AdminAccountId = Guid.Parse("00000000-0000-4000-8000-0000000000a1");

    /// <summary>The identity of alpha's account, fixed so a lookup by key can be tried.</summary>
    private static readonly Guid AlphaAccountId = Guid.Parse("00000000-0000-4000-8000-0000000000a2");

    /// <summary>The identity of beta's account, fixed so a lookup by key can be tried.</summary>
    private static readonly Guid BetaAccountId = Guid.Parse("00000000-0000-4000-8000-0000000000b2");

    /// <summary>The first reseller of the seeded database.</summary>
    private readonly Reseller _alpha = ResellerTestData.Reseller("alpha");

    /// <summary>The second reseller of the seeded database.</summary>
    private readonly Reseller _beta = ResellerTestData.Reseller("beta");

    /// <summary>The in-memory database every context in one test opens.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>The ids of the plans seeded: administrator's, alpha's, beta's.</summary>
    private readonly Guid[] _planIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    /// <summary>A context with no principal shows no account at all.</summary>
    [Fact]
    public async Task An_anonymous_caller_sees_no_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Anonymous());

        Assert.Empty(await dbContext.Accounts.ToListAsync());
        Assert.False(await dbContext.Accounts.AnyAsync());
        Assert.Null(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == AdminAccountId));
    }

    /// <summary>A context with no principal shows no plan at all.</summary>
    [Fact]
    public async Task An_anonymous_caller_sees_no_plan()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Anonymous());

        Assert.Empty(await dbContext.Plans.ToListAsync());
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[0]));
    }

    /// <summary>A context with no principal shows no reseller row at all.</summary>
    [Fact]
    public async Task An_anonymous_caller_sees_no_reseller()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Anonymous());

        Assert.Empty(await dbContext.Resellers.ToListAsync());
        Assert.Null(await dbContext.Resellers.FirstOrDefaultAsync(reseller => reseller.Id == _alpha.Id));
    }

    /// <summary>
    /// The positive control of the anonymous refusals: bypassing the filters on the same anonymous
    /// context shows every row, so the refusals above are the filters at work and not an empty seed.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_that_bypasses_the_filters_sees_every_row()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Anonymous());

        Assert.Equal(3, await dbContext.Accounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await dbContext.Plans.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await dbContext.Resellers.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>A customer login acting for no account sees no account.</summary>
    [Fact]
    public async Task An_accountless_customer_sees_no_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.AccountlessCustomer());

        Assert.Empty(await dbContext.Accounts.ToListAsync());
        Assert.Null(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == AdminAccountId));
    }

    /// <summary>A customer login acting for no account sees no plan.</summary>
    [Fact]
    public async Task An_accountless_customer_sees_no_plan()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.AccountlessCustomer());

        Assert.Empty(await dbContext.Plans.ToListAsync());
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[0]));
    }

    /// <summary>A customer login acting for no account sees no reseller row.</summary>
    [Fact]
    public async Task An_accountless_customer_sees_no_reseller()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.AccountlessCustomer());

        Assert.Empty(await dbContext.Resellers.ToListAsync());
    }

    /// <summary>A customer sees the account it acts for: the positive control.</summary>
    [Fact]
    public async Task A_customer_sees_its_own_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));

        var names = await dbContext.Accounts.Select(account => account.Name).ToListAsync();

        Assert.Equal("alphaaccount", Assert.Single(names));
        Assert.NotNull(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == AlphaAccountId));
    }

    /// <summary>A customer sees no account other than the one it acts for: the refusing control.</summary>
    [Fact]
    public async Task A_customer_does_not_see_any_other_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));

        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Name == "adminaccount"));
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Name == "betaaccount"));
        Assert.Null(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == BetaAccountId));
    }

    /// <summary>A customer sees the plan its own account is on: the positive control.</summary>
    [Fact]
    public async Task A_customer_sees_the_plan_of_its_own_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));

        var ids = await dbContext.Plans.Select(plan => plan.Id).ToListAsync();

        Assert.Equal(new[] { _planIds[1] }, ids);
    }

    /// <summary>A customer sees no plan other than its own account's: the refusing control.</summary>
    [Fact]
    public async Task A_customer_does_not_see_any_other_plan()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));

        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[0]));
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[2]));
        Assert.Null(await dbContext.Plans.FirstOrDefaultAsync(plan => plan.Id == _planIds[2]));
    }

    /// <summary>A customer sees no reseller row, not even the reseller that owns its own account.</summary>
    [Fact]
    public async Task A_customer_sees_no_reseller_not_even_its_own_resellers_row()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));

        Assert.Empty(await dbContext.Resellers.ToListAsync());
        Assert.Null(await dbContext.Resellers.FirstOrDefaultAsync(reseller => reseller.Id == _alpha.Id));
    }

    /// <summary>
    /// A customer of one account cannot read the row of another account by its key, which is the
    /// insecure-direct-object-reference shape: the row is absent, not forbidden.
    /// </summary>
    [Fact]
    public async Task A_customer_of_another_account_cannot_read_the_row_of_the_first_account()
    {
        await SeedAsync();
        await using var owner = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(AlphaAccountId));
        await using var intruder = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Customer(BetaAccountId));

        Assert.NotNull(await owner.Accounts.FirstOrDefaultAsync(account => account.Id == AlphaAccountId));
        Assert.Null(await intruder.Accounts.FirstOrDefaultAsync(account => account.Id == AlphaAccountId));
        Assert.False(await intruder.Plans.AnyAsync(plan => plan.Id == _planIds[1]));
    }

    /// <summary>An administrator sees every account.</summary>
    [Fact]
    public async Task An_administrator_sees_every_account()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());

        Assert.Equal(3, await dbContext.Accounts.CountAsync());
        Assert.NotNull(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == BetaAccountId));
    }

    /// <summary>An administrator sees every plan.</summary>
    [Fact]
    public async Task An_administrator_sees_every_plan()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());

        Assert.Equal(3, await dbContext.Plans.CountAsync());
    }

    /// <summary>An administrator sees every reseller row.</summary>
    [Fact]
    public async Task An_administrator_sees_every_reseller()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());

        Assert.Equal(2, await dbContext.Resellers.CountAsync());
    }

    /// <summary>A reseller still sees only the accounts, plans and reseller row it owns.</summary>
    [Fact]
    public async Task A_reseller_still_sees_only_what_it_owns()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        var accounts = await dbContext.Accounts.Select(account => account.Name).ToListAsync();
        var plans = await dbContext.Plans.Select(plan => plan.Id).ToListAsync();
        var resellers = await dbContext.Resellers.Select(reseller => reseller.Name).ToListAsync();

        Assert.Equal("alphaaccount", Assert.Single(accounts));
        Assert.Equal(_planIds[1], Assert.Single(plans));
        Assert.Equal("alpha", Assert.Single(resellers));
    }

    /// <summary>A reseller does not see the rows of the administrator or of another reseller.</summary>
    [Fact]
    public async Task A_reseller_does_not_see_rows_it_does_not_own()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Id == AdminAccountId));
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Id == BetaAccountId));
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[0]));
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[2]));
        Assert.False(await dbContext.Resellers.AnyAsync(reseller => reseller.Id == _beta.Id));
    }

    /// <summary>
    /// Seeds, as an administrator, two resellers and for each of the administrator, alpha and beta one
    /// plan and one account on it.
    /// </summary>
    /// <returns>A task that completes when the rows are saved.</returns>
    private async Task SeedAsync()
    {
        await using var seed = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());
        seed.Resellers.AddRange(_alpha, _beta);
        seed.Plans.Add(Plan.Define(_planIds[0], "admin-plan", 100, 5, 5, 5, 5, 5, 5));
        seed.Plans.Add(Plan.Define(_planIds[1], "alpha-plan", 100, 5, 5, 5, 5, 5, 5, resellerId: _alpha.Id));
        seed.Plans.Add(Plan.Define(_planIds[2], "beta-plan", 100, 5, 5, 5, 5, 5, 5, resellerId: _beta.Id));
        seed.Accounts.Add(BuildAccount(AdminAccountId, "adminaccount", _planIds[0], null));
        seed.Accounts.Add(BuildAccount(AlphaAccountId, "alphaaccount", _planIds[1], _alpha.Id));
        seed.Accounts.Add(BuildAccount(BetaAccountId, "betaaccount", _planIds[2], _beta.Id));
        await seed.SaveChangesAsync();
    }

    /// <summary>Builds an account.</summary>
    /// <param name="id">The account's identity.</param>
    /// <param name="name">The account's name.</param>
    /// <param name="planId">Its plan.</param>
    /// <param name="resellerId">Its owner, or null for the administrator.</param>
    /// <returns>The account.</returns>
    private static Account BuildAccount(Guid id, string name, Guid planId, Guid? resellerId)
    {
        return new Account(
            id,
            name,
            $"{name}.example.com",
            planId,
            ResellerTestData.CreatedAt,
            resellerId: resellerId);
    }
}
