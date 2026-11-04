using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The Accounts module's own database context against a real PostgreSQL, with no HTTP in between
/// (issue #179): a reseller caller sees the accounts, plans and reseller row it owns and nothing else,
/// whatever a handler asks for — the structural layer under the endpoint isolation tests. The
/// administrator sees every row, a customer its own account and plan, and an anonymous caller nothing
/// (the filters fail closed).
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerAccountsScopeTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerAccountsScopeTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A context opened for a reseller returns its own accounts, plans and reseller row, and a lookup by
    /// the identifier of another reseller's row, or of the administrator's, finds nothing — the same
    /// answer an identifier that exists nowhere gets.
    /// </summary>
    [Fact]
    public async Task The_accounts_context_shows_a_reseller_its_own_rows_and_no_others()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await using var context = Open(new ResellerCustomer(world.Alpha.ResellerId));

        Assert.Equal(
            [world.Alpha.AccountName],
            await context.Accounts.AsNoTracking().Select(account => account.Name).ToListAsync());
        Assert.Equal(
            [world.Alpha.PlanName],
            await context.Plans.AsNoTracking().Select(plan => plan.DisplayNameKey).ToListAsync());
        Assert.Equal(
            [world.Alpha.Name],
            await context.Resellers.AsNoTracking().Select(reseller => reseller.Name).ToListAsync());

        Assert.Null(await context.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == world.Bravo.AccountId));
        Assert.Null(await context.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == world.HouseAccountId));
        Assert.Null(await context.Plans.AsNoTracking().SingleOrDefaultAsync(plan => plan.Id == world.Bravo.PlanId));
        Assert.Null(await context.Plans.AsNoTracking().SingleOrDefaultAsync(plan => plan.Id == world.HousePlanId));
        Assert.Null(await context.Resellers.AsNoTracking().SingleOrDefaultAsync(reseller => reseller.Id == world.Bravo.ResellerId));
    }

    /// <summary>
    /// A context opened for an administrator sees every account, plan and reseller. This is the inverse
    /// control of the reseller test above: the narrowing is the reseller's alone, not the context's.
    /// </summary>
    [Fact]
    public async Task The_accounts_context_shows_an_administrator_every_row()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await using var context = Open(new AdministratorUser());

        Assert.Equal(3, await context.Accounts.CountAsync());
        Assert.Equal(3, await context.Plans.CountAsync());
        Assert.Equal(2, await context.Resellers.CountAsync());
    }

    /// <summary>
    /// A context opened for the caller of unattended work, which holds no principal, shows nothing: the
    /// filters fail closed, and work that really needs rows says so with <c>IgnoreQueryFilters()</c>.
    /// </summary>
    [Fact]
    public async Task The_accounts_context_shows_an_anonymous_caller_nothing()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await using var context = Open(new NoPrincipal());

        Assert.Equal(0, await context.Accounts.CountAsync());
        Assert.Equal(0, await context.Plans.CountAsync());
        Assert.Equal(0, await context.Resellers.CountAsync());
        Assert.Equal(3, await context.Accounts.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>
    /// A context opened for a customer shows the one account it acts for and that account's plan, and no
    /// reseller row.
    /// </summary>
    [Fact]
    public async Task The_accounts_context_shows_a_customer_only_its_own_account_and_plan()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await using var context = Open(new AccountCustomer(world.Alpha.AccountId));

        Assert.Equal(
            [world.Alpha.AccountName],
            await context.Accounts.AsNoTracking().Select(account => account.Name).ToListAsync());
        Assert.Equal(
            [world.Alpha.PlanName],
            await context.Plans.AsNoTracking().Select(plan => plan.DisplayNameKey).ToListAsync());
        Assert.Equal(0, await context.Resellers.CountAsync());
    }

    /// <summary>Opens a context straight onto this test's database, as the given caller.</summary>
    /// <param name="principal">The caller the filters close over.</param>
    /// <returns>A context the caller disposes.</returns>
    private AccountsDbContext Open(ICurrentUser principal)
    {
        return new AccountsDbContext(
            new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(_pg.GetConnectionString()).Options,
            principal);
    }

    /// <summary>The principal of a signed-in server administrator, whose scope is the whole server.</summary>
    private sealed class AdministratorUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; } = new AdministratorCaller(Guid.NewGuid(), "admin");
    }
}
