using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The post-cascade residue audit itself, against the composed panel and real PostgreSQL: what it
/// finds, what it reports as unchecked, and that it is not simply answering "clean" to everything.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> The audit is the whole evidence base for the panel's new claim that a
/// deletion COMPLETED — the claim that used to rest on nothing but an event having been published to
/// modules that were not listening. It had no test of its own. Measured: replacing its answer with a
/// constant "nothing left" left the entire suite green at 2190 passed, which means the guarantee was
/// asserted nowhere and the defect could return under a green gate exactly as it arrived under one.
/// </para>
/// <para>
/// <b>Why it is an integration test.</b> The auditor's subject is the COMPOSED panel: it walks every
/// module's mapping through <c>ModuleRegistry</c> and resolves each context from the request's own
/// scope. A unit test could only hand it a list, which is the thing it exists not to depend on.
/// </para>
/// <para>
/// <b>What it deliberately does not claim.</b> The audit reads this database. An account's crontab,
/// its vhost and its key material are on the host and appear in no table here, so a green audit is
/// never evidence about the machine — that is the polygon suite's job. These tests assert the audit
/// on the axis it can see, and the deletion states the limit on the task it writes.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountResidueAuditTests : IAsyncLifetime
{
    /// <summary>The development-only key the test host is booted with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The account whose rows are audited.</summary>
    private const string AccountName = "residue";

    /// <summary>The domain of the site the account owns.</summary>
    private const string SiteDomain = "residue.example.com";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountResidueAuditTests(PostgresFixture postgres)
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

    /// <summary>The audit names a module that kept the account's rows.</summary>
    /// <remarks>
    /// The row is left in place deliberately, with no cascade run: this is the state a module with
    /// no subscriber leaves behind, and the audit is the only thing between it and a COMPLETED task.
    /// </remarks>
    [Fact]
    public async Task The_audit_names_a_module_that_kept_the_accounts_rows()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory, includeSite: true);

        var residue = await AuditAsync(factory, accountId);

        Assert.Contains("Site(1)", residue.Rows);
        Assert.Empty(residue.Unchecked);
    }

    /// <summary>The audit finds nothing for an account no module holds rows for.</summary>
    /// <remarks>
    /// The inverse control, and the one that makes the test above mean something. An auditor that
    /// reported residue for everything would refuse every deletion in the panel, which is the false
    /// failure that teaches an operator to retry blindly — and it would satisfy every assertion that
    /// only ever hands it a leak.
    /// </remarks>
    [Fact]
    public async Task The_audit_finds_nothing_for_an_account_no_module_holds_rows_for()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory, includeSite: false);

        var residue = await AuditAsync(factory, accountId);

        Assert.Empty(residue.Rows);
        Assert.Empty(residue.Unchecked);
    }

    /// <summary>A module the audit could not read comes back as unchecked rather than as clean.</summary>
    /// <remarks>
    /// The blind spot, asserted rather than described. The audit deliberately skips a module it
    /// cannot read — an audit that could veto a deletion by failing would turn its own outage into an
    /// account nobody can remove — and the honest consequence of skipping is saying which module went
    /// unchecked, so a completion is never read as a clean bill for a module nobody asked. The
    /// failure is manufactured the way the cascade suite manufactures one: the Databases module's
    /// schema is left unmigrated, so its count really does fail against real PostgreSQL.
    /// </remarks>
    [Fact]
    public async Task A_module_the_audit_could_not_read_comes_back_as_unchecked_rather_than_as_clean()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory, includeDatabases: false);
        var accountId = await SeedAsync(factory, includeSite: false);

        var residue = await AuditAsync(factory, accountId);

        Assert.Contains(nameof(DatabasesDbContext), residue.Unchecked);
        Assert.Empty(residue.Rows);
    }

    /// <summary>Runs the audit exactly as the deletion does, from a request's own scope.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account to audit.</param>
    /// <returns>What the audit saw and what it could not see.</returns>
    private static async Task<AccountResidue> AuditAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var auditor = scope.ServiceProvider.GetRequiredService<IAccountResidueAuditor>();

        return await auditor.FindResidueAsync(accountId, CancellationToken.None);
    }

    /// <summary>Applies the modules' migrations, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="includeDatabases">
    /// Whether the Databases module's schema is created. <c>false</c> is how the unchecked test makes
    /// that module's count really fail.
    /// </param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory, bool includeDatabases = true)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();

        if (includeDatabases)
        {
            await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
        }
    }

    /// <summary>Seeds one account, optionally with the site row a module would have kept.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="includeSite">Whether the account is given a site.</param>
    /// <returns>The account's identity.</returns>
    private static async Task<Guid> SeedAsync(WebApplicationFactory<Program> factory, bool includeSite)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), AccountName, SiteDomain, planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        if (includeSite)
        {
            var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            sites.Sites.Add(new Site(
                Guid.NewGuid(),
                account.Id,
                SiteDomain,
                [],
                SiteBackendType.Php,
                "8.3",
                string.Empty,
                $"/home/{AccountName}/sites/{SiteDomain}",
                now));
            await sites.SaveChangesAsync();
        }

        return account.Id;
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory()
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
        });
    }
}
