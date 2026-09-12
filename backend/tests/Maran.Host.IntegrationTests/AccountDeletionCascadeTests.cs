using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
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

    /// <summary>The domain of the site and of the certificate the account owns.</summary>
    private const string SiteDomain = "cascade.example.com";

    /// <summary>The PHP version the account's one site runs, and therefore the pool the deletion retires.</summary>
    private const string PhpVersion = "8.3";

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

    /// <summary>Deleting an account removes its backup rows.</summary>
    /// <remarks>
    /// The Backups module's own unit tests cover its handler, but they exercise it directly. This is
    /// the same cascade through the real deletion command, against a real PostgreSQL, with the
    /// residue auditor in the path — the altitude at which a module that quietly stopped removing
    /// its rows would actually be caught. It is the assertion that was missing when a mutant handler
    /// that deleted nothing left this whole class green.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_removes_its_backup_rows()
    {
        var agent = new StubAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory);

        // The row is there BEFORE the deletion. Without this, an assertion that the rows are gone
        // afterwards passes just as loudly for a fixture that never wrote one — which is precisely
        // how the gap this test closes went unnoticed.
        using (var before = factory.Services.CreateScope())
        {
            Assert.NotEmpty(await Rows<BackupsDbContext, Backup>(before, accountId));
        }

        var result = await DeleteAsync(factory, accountId);

        Assert.True(result.IsSuccess, result.Error?.Code);

        using var scope = factory.Services.CreateScope();
        var remaining = await Rows<BackupsDbContext, Backup>(scope, accountId);

        // EXACTLY ONE row survives, and it is the final backup this very deletion took (spec §12).
        // The cascade removed the seeded manual row and kept the PreDeletion one, which is the whole
        // of Backup.SurvivesAccountDeletion() measured end to end: a cascade that swept everything
        // would delete the last copy of the customer's data in the same breath as the thing it was a
        // copy of, and a cascade that kept everything would leave the residue the auditor refuses
        // over. Asserting the KIND rather than the count alone is what keeps those two apart.
        Assert.Single(remaining);
        Assert.Equal(BackupKind.PreDeletion, remaining[0].Kind);
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

    /// <summary>Deleting an account that owns a site and a certificate leaves neither behind.</summary>
    /// <remarks>
    /// <para>
    /// The fixture is the whole point. The test above seeds a database row and an SFTP row, which
    /// are the two modules that had a subscriber, and it passed for the entire time <c>Sites</c> and
    /// <c>Ssl</c> had none — a live browser run found an ENABLED site, a <c>Certificate</c> row and
    /// <c>privkey.pem</c> surviving an account deletion that reported COMPLETED at 100. A cascade
    /// test whose fixture omits the modules that leak asserts nothing about them and reads as
    /// though it did, which is the vacuity rules/testing.md names.
    /// </para>
    /// <para>
    /// So the account here owns one of everything a v1 account can own, and the assertions are on
    /// both halves: the ROWS are gone from every module's schema, and the agent was actually ORDERED
    /// to take the vhost — and with it the account's certificate material — off the host. The second
    /// half is what a row count cannot see: a handler that removed its rows and told the agent
    /// nothing would satisfy every database assertion and leave a private key on disk.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_that_owns_a_site_and_a_certificate_leaves_neither_behind()
    {
        var agent = new StubAgentAccountsClient();
        var sites = new RecordingAgentSitesClient();
        await using var factory = CreateFactory(agent, sites);
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory, includeWebsite: true);

        var result = await DeleteAsync(factory, accountId);

        Assert.True(result.IsSuccess, result.Error?.Code);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await Rows<SitesDbContext, Site>(scope, accountId));
        Assert.Empty(await Rows<SslDbContext, Certificate>(scope, accountId));
        Assert.Empty(await Rows<DatabasesDbContext, Database>(scope, accountId));
        Assert.Empty(await Rows<SftpDbContext, SftpUser>(scope, accountId));

        // The host half. The retired pool version is asserted too, because the account's only site
        // is the last one on it: an empty string here would leave a php-fpm pool naming a user that
        // no longer resolves, which is the state the agent's own deletion doc calls unrecoverable.
        Assert.Equal([(AccountName, SiteDomain, PhpVersion)], sites.Deleted);

        // And the task said so honestly. A deletion that reported COMPLETED while the rows above
        // survived is the defect this test was written for, so the claim is asserted beside the
        // facts it claims rather than trusted.
        // Past the filter: the Tasks module's global filter is `IsAdmin`, and this scope carries no
        // principal at all, so a filtered read would answer "no such task" for a task that is there.
        // That is the answer that would make this assertion unfalsifiable.
        var task = await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .PanelTasks.IgnoreQueryFilters().SingleAsync(row => row.Kind == TaskKinds.AccountDeletion);
        Assert.Equal(PanelTaskStatus.Completed, task.Status);
    }

    /// <summary>Deleting an account removes its panel login and every credential that login holds.</summary>
    /// <remarks>
    /// <para>
    /// The Identity module was the last one owning rows keyed by an account and handling no cascade,
    /// and the exemption recording that read as harmless because v1 creates no customer login. It was
    /// not harmless in either direction. The residue auditor has no exemption list, so the first
    /// customer login ever created would have made EVERY deletion of its account fail with
    /// <c>AccountCleanupFailed</c> and no way forward for the operator; and the obvious repair —
    /// widening the exemption — would have left a working password against a tenant that was gone.
    /// </para>
    /// <para>
    /// So this test seeds the login that did not exist, and its assertions are on both halves: the
    /// deletion SUCCEEDS, which is the half that was going to break loudly, and the session, the
    /// reset token and the recovery code are gone, which is the half that was going to break
    /// quietly. Those three tables carry a <c>UserId</c> and no <c>AccountId</c>, so the residue
    /// audit cannot see them and this is the only thing that looks.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_removes_its_panel_login_and_every_credential_that_login_holds()
    {
        var agent = new StubAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory, includeCustomerLogin: true);

        using (var before = factory.Services.CreateScope())
        {
            // The fixture is asserted before the act, because every assertion below is satisfied by
            // a seed that never landed — the vacuity this suite has already been bitten by once.
            var seeded = before.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Equal(1, await seeded.Users.CountAsync(user => user.AccountId == accountId));
            Assert.Equal(1, await seeded.Sessions.CountAsync());
            Assert.Equal(1, await seeded.PasswordResetTokens.CountAsync());
            Assert.Equal(1, await seeded.RecoveryCodes.CountAsync());
        }

        var result = await DeleteAsync(factory, accountId);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal([AccountName], agent.Deleted);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Empty(await identity.Users.Where(user => user.AccountId == accountId).ToListAsync());
        Assert.Equal(0, await identity.Sessions.CountAsync());
        Assert.Equal(0, await identity.PasswordResetTokens.CountAsync());
        Assert.Equal(0, await identity.RecoveryCodes.CountAsync());
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
    /// <param name="sites">
    /// The stand-in for the agent's site operations, for the tests whose account owns a site. Left
    /// null by the tests that seed none, so that a cascade which nevertheless called it fails on the
    /// real client's absent socket rather than on a stub that was happy to be asked.
    /// </param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(
        IAgentAccountsClient agent,
        IAgentSitesClient? sites = null)
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAgentAccountsClient>();
                services.AddSingleton(agent);

                // The deletion now takes a final backup BEFORE it releases anything (spec §12), and
                // a failed final backup refuses the deletion. Without a stand-in these tests reach
                // the real gRPC client, which has no socket here, and every one of them fails with
                // Unavailable over an account that is now undeletable — the honest behaviour of a
                // panel whose agent is down, and not what these tests are about.
                services.RemoveAll<IAgentBackupClient>();
                services.AddSingleton<IAgentBackupClient>(new StubAgentBackupClient());

                if (sites is not null)
                {
                    services.RemoveAll<IAgentSitesClient>();
                    services.AddSingleton(sites);
                }
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

        // Derived from ModuleRegistry, not written down here: the cascade and the residue audit are
        // properties of the COMPOSED panel, so the set of schemas a deletion has to reach is the
        // registry's set and nothing smaller. A hand-written list broke this fixture when Backups
        // joined and again when Ftp joined, each time by reporting the new module as UNCHECKED, and
        // each time it was repaired by appending one line. Now the next module joins by
        // construction.
        //
        // The single exclusion: the Databases module's schema, left uncreated so its subscriber
        // really fails against real PostgreSQL, which is how the abort test manufactures a failing
        // module. An exclusion the registry no longer declares fails by name.
        if (includeDatabases)
        {
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
        }
        else
        {
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider, typeof(DatabasesDbContext));
        }

        // The default backup destination, which a real server has because the installer migrates
        // before the panel boots and the reconciliation runs at boot. In this fixture the order is
        // reversed — the host is built first and the schema arrives here — so the hosted task found
        // no tables, logged, and swallowed, exactly as it is written to. Without this line the final
        // backup that guards every deletion (spec §12) refuses with BackupDestinationNotConfigured
        // and the account survives, which is the RIGHT behaviour on a panel that genuinely has
        // nowhere to put the copy, and the wrong fixture for measuring the cascade.
        await scope.ServiceProvider.GetRequiredService<DefaultBackupDestinationSeeder>()
            .SeedAsync(CancellationToken.None);
    }

    /// <summary>Seeds one account with a database row and an SFTP login row.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="includeDatabase">Whether a database row is written; <c>false</c> when its schema is absent.</param>
    /// <param name="includeWebsite">
    /// Whether the account is also given a site and a certificate for it. Off by default so that the
    /// two older tests keep the fixtures their assertions were written against, and ON for the test
    /// that exists because a fixture without them proved nothing about the modules that leaked.
    /// </param>
    /// <param name="includeCustomerLogin">
    /// Whether the account is also given a panel login with a session, a reset token and a recovery
    /// code. Off by default, because it is the row v1 never creates and the older tests describe the
    /// panel as it is; ON for the test of the module that had no subscriber.
    /// </param>
    /// <returns>The account's identity.</returns>
    private static async Task<Guid> SeedAsync(
        WebApplicationFactory<Program> factory,
        bool includeDatabase = true,
        bool includeWebsite = false,
        bool includeCustomerLogin = false)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), AccountName, "cascade.example.com", planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        sftp.SftpUsers.Add(new SftpUser(Guid.NewGuid(), account.Id, "web", $"{AccountName}_web", now));
        await sftp.SaveChangesAsync();

        // Unconditional, like the SFTP row and unlike the opt-in ones below, because without it
        // NOTHING at this altitude observes the Backups cascade at all. Measured: with the module's
        // AccountDeleting handler mutated to delete nothing, every test in this class and in
        // AccountResidueAuditTests stayed GREEN — the fixtures held no backup row, so the residue
        // auditor had nothing to find and reported the module clean because it was empty, not
        // because the cascade worked. That is the vacuous-gate shape rules/testing.md names: the
        // check could not have seen the defect it exists for. This row is what gives it something
        // to see, and it makes every cascade test in this class carry the observation.
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var backup = new Backup(Guid.NewGuid(), account.Id, destinationId: null, BackupKind.Manual, now);
        backup.Completed(4096, new string('a', 64), 1, now);
        backups.Backups.Add(backup);
        await backups.SaveChangesAsync();

        if (includeWebsite)
        {
            await SeedWebsiteAsync(scope, account.Id, now);
        }

        if (includeCustomerLogin)
        {
            await SeedCustomerLoginAsync(scope, account.Id, now);
        }

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

    /// <summary>Gives the account one panel login, with every credential such a login carries.</summary>
    /// <param name="scope">The scope the module contexts are resolved from.</param>
    /// <param name="accountId">The account the login owns.</param>
    /// <param name="now">The injected instant every row is stamped with.</param>
    /// <remarks>
    /// The role is <c>Customer</c> and the login owns the account, which is exactly the row the
    /// product does not yet construct. Writing it here is what makes the cascade testable before the
    /// feature that creates it exists — the alternative is discovering the behaviour on the day a
    /// customer login ships, on a live panel, with deletions failing.
    /// </remarks>
    private static async Task SeedCustomerLoginAsync(IServiceScope scope, Guid accountId, DateTimeOffset now)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = new User(
            Guid.NewGuid(), AccountName, $"{AccountName}@example.com", "hash", UserRole.Customer, now);
        user.AssignAccount(accountId);
        identity.Users.Add(user);

        identity.Sessions.Add(new Session(
            Guid.NewGuid(),
            user.Id,
            Guid.NewGuid(),
            "cascade-refresh-token-digest",
            now,
            now.AddDays(30),
            "203.0.113.10",
            "integration-tests"));
        identity.PasswordResetTokens.Add(new PasswordResetToken(
            Guid.NewGuid(), user.Id, "cascade-reset-token-digest", now));
        identity.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), user.Id, "cascade-recovery-code-digest"));

        await identity.SaveChangesAsync();
    }

    /// <summary>Gives the account one PHP site and one certificate installed for it.</summary>
    /// <param name="scope">The scope the module contexts are resolved from.</param>
    /// <param name="accountId">The account that owns both.</param>
    /// <param name="now">The injected instant both rows are stamped with.</param>
    private static async Task SeedWebsiteAsync(IServiceScope scope, Guid accountId, DateTimeOffset now)
    {
        var siteId = Guid.NewGuid();
        var siteContext = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        siteContext.Sites.Add(new Site(
            siteId,
            accountId,
            SiteDomain,
            [],
            SiteBackendType.Php,
            PhpVersion,
            string.Empty,
            $"/home/{AccountName}/sites/{SiteDomain}",
            now));
        await siteContext.SaveChangesAsync();

        var ssl = scope.ServiceProvider.GetRequiredService<SslDbContext>();
        ssl.Certificates.Add(new Certificate(
            Guid.NewGuid(),
            accountId,
            siteId,
            SiteDomain,
            CertificateSource.Custom,
            now.AddDays(90),
            now));
        await ssl.SaveChangesAsync();
    }
}
