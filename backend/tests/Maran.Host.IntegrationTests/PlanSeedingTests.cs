using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a freshly installed panel has in its database once it has started: the standard plans, so
/// the very first thing an operator does — create an account — is possible at all.
/// </summary>
/// <remarks>
/// The order here is the installer's order and is the whole point of the test. The migrations are
/// applied to an empty database BEFORE the host is built, and only then is the host started, so
/// what is measured is what the running panel did to a migrated database — not what a seeder does
/// when a test calls it.
///
/// That distinction is the defect this test closes. <see cref="PlanSeeder"/> had a unit test that
/// constructed it and called it, and the unit test passed for as long as the seeder had no caller
/// anywhere in the product. A fresh server therefore had no plans, <c>Account.PlanId</c> carries a
/// foreign key to the plans table, and account creation was impossible on every new installation
/// while 758 tests stayed green.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PlanSeedingTests : IAsyncLifetime
{
    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PlanSeedingTests(PostgresFixture postgres)
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

    /// <summary>A freshly installed panel has the standard plans once it has started.</summary>
    [Fact]
    public async Task A_freshly_installed_panel_has_the_standard_plans_once_it_has_started()
    {
        await MigrateAsync();

        await using var factory = BuildHost();

        var seeded = await ReadPlanIdsAsync(factory);

        Assert.Contains(PlanSeeder.StarterPlanId, seeded);
        Assert.Contains(PlanSeeder.BusinessPlanId, seeded);
        Assert.Contains(PlanSeeder.UnlimitedPlanId, seeded);
    }

    /// <summary>A restart leaves an operators edited plan exactly as they edited it.</summary>
    /// <remarks>
    /// Seeding that ran on every start and OVERWROTE would be worse than seeding that never ran: an
    /// operator who lowered the Business plan's disk quota would find it silently restored the next
    /// time the panel was restarted, and nothing would say so. So the second start is asserted to
    /// insert nothing and change nothing.
    /// </remarks>
    [Fact]
    public async Task A_restart_leaves_an_operators_edited_plan_exactly_as_they_edited_it()
    {
        await MigrateAsync();

        await using (var first = BuildHost())
        {
            _ = await ReadPlanIdsAsync(first);
        }

        const int EditedQuota = 12_345;
        await using (var context = OpenContext())
        {
            var business = await context.Plans.SingleAsync(plan => plan.Id == PlanSeeder.BusinessPlanId);
            // Through EF's own metadata, because a plan's properties are private-set: the panel has
            // no edit-a-plan operation yet, and this test is about what a RESTART does to a row an
            // operator has changed, not about how they changed it.
            context.Entry(business).Property(nameof(Plan.DiskQuotaMb)).CurrentValue = EditedQuota;
            await context.SaveChangesAsync();
        }

        await using (var second = BuildHost())
        {
            _ = await ReadPlanIdsAsync(second);
        }

        await using var after = OpenContext();
        var reread = await after.Plans.AsNoTracking().SingleAsync(plan => plan.Id == PlanSeeder.BusinessPlanId);

        Assert.Equal(EditedQuota, reread.DiskQuotaMb);
        Assert.Equal(3, await after.Plans.CountAsync());
    }

    /// <summary>Applies the Accounts migrations to the empty database, the way the installer does.</summary>
    private async Task MigrateAsync()
    {
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Opens a context straight onto this test's database, outside any host.</summary>
    /// <returns>A context the caller disposes.</returns>
    private AccountsDbContext OpenContext()
    {
        return new AccountsDbContext(
            new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);
    }

    /// <summary>Builds a host against this test's database, exactly as the panel is configured.</summary>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> BuildHost()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", EncryptionKey);
            builder.UseSetting("Jwt:SigningKey", EncryptionKey);

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>
    /// Starts the host and reads the plan ids back through the panel's own container.
    /// </summary>
    /// <param name="factory">The host to start.</param>
    /// <returns>The ids of every plan in the database once the host has started.</returns>
    /// <remarks>
    /// Touching <c>factory.Services</c> is what builds and starts the host, which is what runs the
    /// hosted services — so the read below happens after startup has completed, not alongside it.
    /// </remarks>
    private static async Task<IReadOnlyList<Guid>> ReadPlanIdsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        return await context.Plans.AsNoTracking().Select(plan => plan.Id).ToListAsync();
    }
}
