using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Domain;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Domain;
using Maran.Modules.Sftp.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The account-deletion cascade across module boundaries, over the real message bus and real
/// PostgreSQL: deleting an account takes the Databases and Sftp modules' rows with it, and a module
/// that refuses abandons the deletion with the account intact.
/// </summary>
/// <remarks>
/// <para>
/// It has to be an integration test. The cascade's whole mechanism is a Wolverine message published
/// by one module and handled by two others that the publishing module may not reference
/// (rules/architecture.md, enforced by <c>ModuleIsolationTests</c>), so nothing short of a booted
/// host with the real bus can show that the handlers are discovered, invoked INLINE, and able to
/// stop the deletion by failing. A unit test of the Accounts handler could only show that it called
/// a bus.
/// </para>
/// <para>
/// The defect this closes: <c>userdel</c> touches neither MySQL nor sshd, so deleting <c>alice</c>
/// left every <c>alice_*</c> row in the panel and every <c>alice_*</c> database and login on the
/// host. System user names are recycled, so an account created again under the same name inherited
/// the previous tenant's live data and a working credential into it. The host half is proved by
/// <c>account_deletion_on_a_real_host.rs</c>; this is the panel half.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountDeletionCascadeTests : IAsyncLifetime
{
    /// <summary>The development-only key the test host is booted with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The account under test, and the name a later tenant would be given again.</summary>
    private const string AccountName = "cascade";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountDeletionCascadeTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Deleting an account removes its database rows and its sftp rows.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_its_database_rows_and_its_sftp_rows()
    {
        var agent = new StubAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory);

        var result = await DeleteAsync(factory, accountId);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal([AccountName], agent.Deleted);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await Rows<DatabasesDbContext, Database>(scope, accountId));
        Assert.Empty(await Rows<SftpDbContext, SftpUser>(scope, accountId));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AccountsDbContext>()
            .Accounts.IgnoreQueryFilters().Where(row => row.Id == accountId).ToListAsync());
    }

    /// <summary>A cleanup failure aborts the deletion and leaves the account recoverable.</summary>
    [Fact]
    public async Task A_cleanup_failure_aborts_the_deletion_and_leaves_the_account_recoverable()
    {
        // The failure is manufactured by leaving the Databases module's schema unmigrated, so its
        // subscriber really does fail against a real PostgreSQL — a stub that was told to throw
        // would prove only that the try/catch compiles. Everything else is migrated, so the
        // account, its user and its SFTP row are all really there to be left alone.
        var agent = new StubAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory, includeDatabases: false);
        var accountId = await SeedAsync(factory, includeDatabase: false);

        var result = await DeleteAsync(factory, accountId);

        // The named code, not merely "it failed": an exit status is not evidence of WHICH control
        // fired, and this must be the cleanup abort rather than, say, AccountNotFound.
        Assert.False(result.IsSuccess);
        Assert.Equal("AccountCleanupFailed", result.Error?.Code);

        // The agent was never asked. This is the assertion that pins the ORDER: the cascade can
        // only abort a deletion if it runs BEFORE the host is touched.
        Assert.Empty(agent.Deleted);

        using var scope = factory.Services.CreateScope();
        var accounts = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>()
            .Accounts.IgnoreQueryFilters().Where(row => row.Id == accountId).ToListAsync();
        Assert.Single(accounts);

        // And nothing else was half-removed either: a deletion that took some modules' rows before
        // failing would leave the "recoverable" account pointing at nothing.
        Assert.Single(await Rows<SftpDbContext, SftpUser>(scope, accountId));
    }

    /// <summary>Every row a module holds for an account, read past the tenant filter.</summary>
    /// <typeparam name="TContext">The module's database context.</typeparam>
    /// <typeparam name="TRow">The module's tenant-scoped entity.</typeparam>
    /// <param name="scope">A scope to resolve the context from.</param>
    /// <param name="accountId">The account whose rows are counted.</param>
    /// <returns>The surviving rows.</returns>
    private static async Task<List<TRow>> Rows<TContext, TRow>(IServiceScope scope, Guid accountId)
        where TContext : DbContext
        where TRow : class
    {
        // Past the filter deliberately: the question is whether the ROWS are gone from the schema,
        // and a filtered read would answer "empty" for rows that are merely invisible to whoever
        // this context thinks is asking.
        return await scope.ServiceProvider.GetRequiredService<TContext>()
            .Set<TRow>()
            .IgnoreQueryFilters()
            .Where(row => EF.Property<Guid>(row, "AccountId") == accountId)
            .ToListAsync();
    }

    /// <summary>Runs the deletion through the real bus, exactly as the controller does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account to remove.</param>
    /// <returns>What the handler answered.</returns>
    private static async Task<Result<ulong>> DeleteAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        return await bus.InvokeAsync<Result<ulong>>(new DeleteAccountCommand(accountId, "203.0.113.10", "integration-tests"), CancellationToken.None);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent stubbed.</summary>
    /// <param name="agent">The stand-in for the agent's account operations.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(IAgentAccountsClient agent)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAgentAccountsClient>();
                services.AddSingleton(agent);
            });
        });
    }

    /// <summary>Applies the modules' migrations, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="includeDatabases">
    /// Whether the Databases module's schema is created. <c>false</c> is how the abort test makes
    /// that module's subscriber really fail.
    /// </param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory, bool includeDatabases = true)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();

        if (includeDatabases)
        {
            await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
        }
    }

    /// <summary>Seeds one account with a database row and an SFTP login row.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="includeDatabase">Whether a database row is written; <c>false</c> when its schema is absent.</param>
    /// <returns>The account's identity.</returns>
    private static async Task<Guid> SeedAsync(WebApplicationFactory<Program> factory, bool includeDatabase = true)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5));
        var account = new Account(Guid.NewGuid(), AccountName, "cascade.example.com", planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        sftp.SftpUsers.Add(new SftpUser(Guid.NewGuid(), account.Id, "web", $"{AccountName}_web", now));
        await sftp.SaveChangesAsync();

        if (includeDatabase)
        {
            var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
            databases.Databases.Add(new Database(
                Guid.NewGuid(),
                account.Id,
                "shop",
                $"{AccountName}_shop",
                $"{AccountName}_shopuser",
                "shopuser",
                now));
            await databases.SaveChangesAsync();
        }

        return account.Id;
    }
}
