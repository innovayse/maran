using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A reseller reaches no customer data directly (issue #179, decision D8): on Sites, Ssl, Tasks, Cron,
/// Ftp and Sftp a reseller token sees no rows, even for an account the reseller OWNS, and reaches a
/// customer's resources only through a view-as session, which is journalled, time-boxed and revocable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The check can see what it reports on.</b> The reseller owns <c>acme</c>, and one row of every
/// module is stored against it, so an empty answer is the module's scoping and not an empty table; the
/// customer who owns <c>acme</c> reads each of those rows through the same endpoint, which is the
/// positive control for every assertion of an empty answer.
/// </para>
/// <para>
/// UNOBSERVED HERE: the agent. The cron entries live in an account's crontab, so a double stands in
/// for it, and the assertion that matters is that it was never addressed on the reseller's behalf.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerTenantModuleScopeEndpointTests : IAsyncLifetime
{
    /// <summary>The paged list endpoints of the tenant modules a reseller must see nothing of.</summary>
    private static readonly string[] PagedLists =
    [
        "/api/v1/sites",
        "/api/v1/certificates",
        "/api/v1/tasks",
        "/api/v1/ftp-users",
        "/api/v1/sftp-users",
    ];

    /// <summary>A fixed instant the seeded rows are created at.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The cron agent double every request is served by.</summary>
    private readonly StubAgentCronClient _cron = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerTenantModuleScopeEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// The customer who owns the account reads its row on every module, the administrator reads it too, and
    /// the reseller that owns the account reads none: not one row on any list.
    /// </summary>
    [Fact]
    public async Task A_reseller_sees_no_rows_in_the_tenant_modules_even_for_an_account_it_owns()
    {
        var (factory, _) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var olive = await EndpointHarness.SignInAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var seen = new List<string>();
        foreach (var path in PagedLists)
        {
            Assert.Equal(1, await TotalAsync(olive, path));
            Assert.Equal(1, await TotalAsync(admin, path));

            var total = await TotalAsync(nova, path);
            if (total != 0)
            {
                seen.Add($"GET {path} showed the reseller {total} row(s)");
            }
        }

        Assert.Empty(seen);
    }

    /// <summary>
    /// Naming one of those rows by id is the same answer as naming one that is not there: 404, and never a
    /// 403 that would confirm the row exists.
    /// </summary>
    [Fact]
    public async Task A_reseller_naming_a_row_of_its_own_customer_is_answered_not_found()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var olive = await EndpointHarness.SignInAsync(factory, "olive");
        Guid siteId;
        Guid ftpId;
        using (var scope = factory.Services.CreateScope())
        {
            siteId = await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Sites
                .Where(site => site.AccountId == world.Acme).Select(site => site.Id).SingleAsync();
            ftpId = await scope.ServiceProvider.GetRequiredService<FtpDbContext>().FtpUsers
                .Where(user => user.AccountId == world.Acme).Select(user => user.Id).SingleAsync();
        }

        var wrong = new List<string>();
        foreach (var path in new[] { $"/api/v1/sites/{siteId}", $"/api/v1/ftp-users/{ftpId}" })
        {
            Assert.Equal(HttpStatusCode.OK, (await olive.GetAsync(path)).StatusCode);
            var response = await nova.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                wrong.Add($"GET {path} answered {(int)response.StatusCode}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The cron routes name an account and keep no rows of their own, so what scopes them is the account
    /// directory: a reseller naming its own customer's account gets the same 404 a stranger does, and the
    /// agent is never addressed for it. The customer's own request reaches the crontab, which is the control.
    /// </summary>
    [Fact]
    public async Task A_reseller_cannot_reach_its_customers_crontab()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var olive = await EndpointHarness.SignInAsync(factory, "olive");
        var path = $"/api/v1/cron-entries?accountId={world.Acme}";

        Assert.Equal(HttpStatusCode.OK, (await olive.GetAsync(path)).StatusCode);
        _cron.AddressedAccounts.Clear();

        var read = await nova.GetAsync(path);
        var written = await nova.PostAsJsonAsync("/api/v1/cron-entries", new
        {
            accountId = world.Acme,
            schedule = new { minute = "0", hour = "3", dayOfMonth = "*", month = "*", dayOfWeek = "*" },
            command = "/usr/bin/backup",
        });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, written.StatusCode);
        Assert.Empty(_cron.AddressedAccounts);
    }

    /// <summary>
    /// The reseller's access to a customer is the view-as session: through it the same rows are read, which
    /// shows the refusals above are the reseller's token and not a broken endpoint.
    /// </summary>
    [Fact]
    public async Task A_reseller_reaches_its_customers_rows_only_through_a_view_as_session()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);

        foreach (var path in PagedLists)
        {
            Assert.Equal(1, await TotalAsync(viewing, path));
        }
    }

    /// <summary>Reads the total of a paged list the caller is entitled to read.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The list's path.</param>
    /// <returns>How many rows the caller's scope holds.</returns>
    private static async Task<int> TotalAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)response.StatusCode}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("total").GetInt32();
    }

    /// <summary>
    /// Boots the host with the view-as world, the extra module databases, and one row of every tenant
    /// module stored against <c>acme</c>, the account the reseller <c>nova</c> owns.
    /// </summary>
    /// <returns>The booted factory, which the caller disposes, and the identities the tests address.</returns>
    private async Task<(WebApplicationFactory<Program> Factory, ViewAsWorld World)> BootAsync()
    {
        var factory = await IdentityResellerWorld.BootAsync(
            _pg,
            configure: services =>
            {
                services.AddSingleton<IAgentCronClient>(_cron);
            });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<CronDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<FtpDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        }

        var world = await IdentityResellerWorld.SeedViewAsWorldAsync(factory);
        await SeedRowsAsync(factory, world.Acme);

        return (factory, world);
    }

    /// <summary>Stores one row of every tenant module against an account.</summary>
    /// <remarks>
    /// Written through contexts resolved outside a request, so no tenant filter separates the seed: the
    /// scoping under test is the only thing that can.
    /// </remarks>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account the rows belong to.</param>
    private static async Task SeedRowsAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var site = new Site(
            Guid.NewGuid(),
            accountId,
            "acme.example.com",
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acme/sites/acme.example.com",
            Epoch);
        sites.Sites.Add(site);
        await sites.SaveChangesAsync();

        var ssl = scope.ServiceProvider.GetRequiredService<SslDbContext>();
        ssl.Certificates.Add(new Certificate(
            Guid.NewGuid(), accountId, site.Id, "acme.example.com", CertificateSource.Acme, Epoch.AddDays(60), Epoch));
        await ssl.SaveChangesAsync();

        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        tasks.PanelTasks.Add(new PanelTask(
            Guid.NewGuid(),
            TaskKinds.CertificateIssue,
            "acme.example.com",
            accountId,
            TaskAudience.AccountOwner,
            null,
            Epoch));
        await tasks.SaveChangesAsync();

        var ftp = scope.ServiceProvider.GetRequiredService<FtpDbContext>();
        ftp.FtpUsers.Add(new FtpUser(Guid.NewGuid(), accountId, "web", "acme_web", Epoch));
        await ftp.SaveChangesAsync();

        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        sftp.SftpUsers.Add(new SftpUser(Guid.NewGuid(), accountId, "web", "acme_web", Epoch));
        await sftp.SaveChangesAsync();
    }
}
