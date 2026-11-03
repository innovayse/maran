using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Persistence;

/// <summary>
/// The narrowing query filters of <see cref="AccountsDbContext"/>: a reseller's context shows it the
/// accounts, plans and reseller rows it owns and no others, and no other kind of caller is narrowed.
/// Every refusing control has its accepting control beside it, so a filter that hid everything, or
/// nothing, would fail one of them.
/// </summary>
public sealed class AccountsDbContextResellerFilterTests
{
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

    /// <summary>A reseller's context shows the accounts it owns: the positive control.</summary>
    [Fact]
    public async Task A_reseller_sees_its_own_accounts()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        var names = await dbContext.Accounts.Select(account => account.Name).ToListAsync();

        Assert.Equal(new[] { "alphaaccount" }, names);
    }

    /// <summary>A reseller's context hides other owners' accounts: the refusing control.</summary>
    [Fact]
    public async Task A_reseller_does_not_see_another_resellers_or_the_administrators_accounts()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Name == "betaaccount"));
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Name == "adminaccount"));
        Assert.Null(await dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == BetaAccountId));
    }

    /// <summary>A reseller's context shows the plans it owns: the positive control.</summary>
    [Fact]
    public async Task A_reseller_sees_its_own_plans()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        var ids = await dbContext.Plans.Select(plan => plan.Id).ToListAsync();

        Assert.Equal(new[] { _planIds[1] }, ids);
    }

    /// <summary>A reseller's context hides other owners' plans: the refusing control.</summary>
    [Fact]
    public async Task A_reseller_does_not_see_another_resellers_or_the_administrators_plans()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[0]));
        Assert.False(await dbContext.Plans.AnyAsync(plan => plan.Id == _planIds[2]));
        Assert.Null(await dbContext.Plans.FirstOrDefaultAsync(plan => plan.Id == _planIds[2]));
    }

    /// <summary>A reseller's context shows its own reseller row: the positive control.</summary>
    [Fact]
    public async Task A_reseller_sees_its_own_reseller_row()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        var names = await dbContext.Resellers.Select(reseller => reseller.Name).ToListAsync();

        Assert.Equal(new[] { "alpha" }, names);
    }

    /// <summary>A reseller's context hides every other reseller row, so it cannot learn that one exists.</summary>
    [Fact]
    public async Task A_reseller_does_not_see_another_reseller_row()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));

        Assert.False(await dbContext.Resellers.AnyAsync(reseller => reseller.Id == _beta.Id));
        Assert.Null(await dbContext.Resellers.FirstOrDefaultAsync(reseller => reseller.Id == _beta.Id));
    }

    /// <summary>A reseller that owns nothing sees nothing, not the administrator's rows.</summary>
    [Fact]
    public async Task A_reseller_that_owns_nothing_sees_no_row_of_any_kind()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(
            _database,
            FakeCurrentUser.Reseller(Guid.NewGuid()));

        Assert.Empty(await dbContext.Accounts.ToListAsync());
        Assert.Empty(await dbContext.Plans.ToListAsync());
        Assert.Empty(await dbContext.Resellers.ToListAsync());
    }

    /// <summary>An administrator is not narrowed: it sees every account, plan and reseller.</summary>
    [Fact]
    public async Task An_administrator_is_not_narrowed()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());

        await AssertSeesEverythingAsync(dbContext);
    }

    /// <summary>A customer is not narrowed by the reseller filters: they leave it as it was.</summary>
    [Fact]
    public async Task A_customer_is_not_narrowed_by_the_reseller_filters()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(
            _database,
            FakeCurrentUser.Customer(Guid.NewGuid()));

        await AssertSeesEverythingAsync(dbContext);
    }

    /// <summary>A customer login acting for no account is not narrowed by the reseller filters.</summary>
    [Fact]
    public async Task An_accountless_customer_is_not_narrowed_by_the_reseller_filters()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.AccountlessCustomer());

        await AssertSeesEverythingAsync(dbContext);
    }

    /// <summary>Unattended work, with no principal, is not narrowed by the reseller filters.</summary>
    [Fact]
    public async Task Unattended_work_is_not_narrowed_by_the_reseller_filters()
    {
        await SeedAsync();
        await using var dbContext = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Anonymous());

        await AssertSeesEverythingAsync(dbContext);
    }

    /// <summary>The filter is evaluated per context, so two resellers on one database see different rows.</summary>
    [Fact]
    public async Task Two_resellers_on_one_database_see_disjoint_rows()
    {
        await SeedAsync();
        await using var alpha = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_alpha.Id));
        await using var beta = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Reseller(_beta.Id));

        var alphaNames = await alpha.Accounts.Select(account => account.Name).ToListAsync();
        var betaNames = await beta.Accounts.Select(account => account.Name).ToListAsync();

        Assert.Equal(new[] { "alphaaccount" }, alphaNames);
        Assert.Equal(new[] { "betaaccount" }, betaNames);
    }

    /// <summary>Asserts the context shows all three accounts, all three plans and both resellers.</summary>
    /// <param name="dbContext">The context under test.</param>
    private static async Task AssertSeesEverythingAsync(AccountsDbContext dbContext)
    {
        Assert.Equal(3, await dbContext.Accounts.CountAsync());
        Assert.Equal(3, await dbContext.Plans.CountAsync());
        Assert.Equal(2, await dbContext.Resellers.CountAsync());
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
        seed.Accounts.Add(Account(Guid.NewGuid(), "adminaccount", _planIds[0], null));
        seed.Accounts.Add(Account(Guid.NewGuid(), "alphaaccount", _planIds[1], _alpha.Id));
        seed.Accounts.Add(Account(BetaAccountId, "betaaccount", _planIds[2], _beta.Id));
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
