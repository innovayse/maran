using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The Accounts module's own database context against a real PostgreSQL, with no HTTP in between
/// (issue #179): a reseller caller sees the accounts, plans and reseller row it owns and nothing else,
/// whatever a handler asks for — the structural layer under the endpoint isolation tests — and every
/// other caller is not narrowed, which is the stated limit of the filter.
/// </summary>
/// <remarks>
/// The second test is a statement of what the filter does NOT do, kept as a test so the limit cannot
/// drift unnoticed: the filters only narrow a reseller, and the other callers are scoped by the
/// controllers' policies and the account directory. Making the context fail closed for them is a separate
/// hardening, and the day it lands this test is what changes with it.
/// </remarks>
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
    /// A context opened for the caller of unattended work, or for a customer, is not narrowed: it sees
    /// every account, plan and reseller. This is the inverse control of the test above — the filter
    /// narrows only a reseller — and the limit the module's remarks state.
    /// </summary>
    /// <param name="caller">Which caller opens the context.</param>
    [Theory]
    [InlineData("anonymous")]
    [InlineData("customer")]
    public async Task The_accounts_context_does_not_narrow_a_caller_that_is_not_a_reseller(string caller)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        ICurrentUser principal = caller == "customer" ? new AccountCustomer(world.Alpha.AccountId) : new NoPrincipal();
        await using var context = Open(principal);

        Assert.Equal(3, await context.Accounts.CountAsync());
        Assert.Equal(3, await context.Plans.CountAsync());
        Assert.Equal(2, await context.Resellers.CountAsync());
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
}
