using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged backup history (<c>GET /api/v1/backups</c>) and destinations list
/// (<c>GET /api/v1/backup-destinations</c>) over real HTTP against a real PostgreSQL: the history's
/// search is an <c>ILIKE</c> only PostgreSQL runs, its owner half crosses into the Accounts module
/// through the real <c>AccountDirectory</c>, and every bound is refused by a validator before a
/// handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class BackupListEndpointTests : IAsyncLifetime
{
    /// <summary>The start instant the seeded backups count from.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupListEndpointTests(PostgresFixture postgres)
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
    /// A search reaches the owner's name and a deleted account's recorded username, literally: an
    /// underscore is not a wildcard, and LIKE metacharacters with a quote and SQL match nothing.
    /// </summary>
    [Fact]
    public async Task A_search_matches_owner_names_and_orphaned_usernames_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var olive = await SeedWorldAsync(factory, "olive");
        var (_, pepper) = await EndpointHarness.SeedAccountAsync(factory, "pepper");
        var orphan = NewBackup(Guid.NewGuid(), Epoch);
        orphan.Orphan("gone_01");
        await SeedBackupsAsync(factory, NewBackup(olive, Epoch), NewBackup(pepper, Epoch), orphan);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byOwner = await GetJsonAsync(admin, "/api/v1/backups?search=LIV");
        using var byOrphan = await GetJsonAsync(admin, "/api/v1/backups?search=gone_");
        using var wildcard = await GetJsonAsync(admin, "/api/v1/backups?search=" + Uri.EscapeDataString("ol_ve"));
        using var hostile = await GetJsonAsync(admin, "/api/v1/backups?search=" + Uri.EscapeDataString("%_\\'; drop"));

        Assert.Equal(1, byOwner.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("olive", Assert.Single(Items(byOwner)).GetProperty("ownerName").GetString());
        Assert.Equal(
            "gone_01",
            Assert.Single(Items(byOrphan)).GetProperty("orphanedAccountUsername").GetString());
        Assert.Equal(0, wildcard.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The IDOR case: a customer searching or filtering for another tenant's backups is answered an
    /// empty page with a total of zero, while the administrator's answer to the same request is not.
    /// </summary>
    [Fact]
    public async Task A_customer_searching_or_filtering_for_another_tenant_gets_a_total_of_zero()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);
        var ownBackup = NewBackup(own, Epoch);
        await SeedBackupsAsync(factory, ownBackup, NewBackup(stranger, Epoch));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminByOwner = await GetJsonAsync(admin, "/api/v1/backups?search=stranger");
        using var byOwner = await GetJsonAsync(customer, "/api/v1/backups?search=stranger");
        using var byAccount = await GetJsonAsync(customer, $"/api/v1/backups?accountId={stranger}");
        using var mine = await GetJsonAsync(customer, "/api/v1/backups");

        Assert.Equal(1, adminByOwner.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byOwner.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byOwner.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, byAccount.RootElement.GetProperty("total").GetInt32());
        Assert.Equal([ownBackup.Id], Ids(mine));
    }

    /// <summary>
    /// The typed filters bind from the query string in the camelCase the answers carry, and the date
    /// window is inclusive at both ends.
    /// </summary>
    [Fact]
    public async Task The_status_kind_and_date_filters_bind_and_keep_only_their_rows()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        var early = NewBackup(owner, Epoch);
        var failed = new Backup(Guid.NewGuid(), owner, null, BackupKind.PreDeletion, Epoch.AddDays(2));
        failed.Failed("AgentSystemFailure", Epoch.AddDays(2));
        var late = NewBackup(owner, Epoch.AddDays(5));
        await SeedBackupsAsync(factory, early, failed, late);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byStatus = await GetJsonAsync(admin, "/api/v1/backups?status=failed");
        using var byKind = await GetJsonAsync(admin, "/api/v1/backups?kind=preDeletion");
        using var byWindow = await GetJsonAsync(
            admin,
            "/api/v1/backups?from=" + Uri.EscapeDataString(Epoch.AddDays(2).ToString("O"))
            + "&to=" + Uri.EscapeDataString(Epoch.AddDays(5).ToString("O")));

        Assert.Equal([failed.Id], Ids(byStatus));
        Assert.Equal([failed.Id], Ids(byKind));
        Assert.Equal([late.Id, failed.Id], Ids(byWindow));
    }

    /// <summary>
    /// Newest first by default, every sort key orders on PostgreSQL in both directions, and a page
    /// past the end is empty with the true total.
    /// </summary>
    [Fact]
    public async Task The_default_order_the_size_sort_and_a_page_past_the_end_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        var older = NewBackup(owner, Epoch, sizeBytes: 9_000);
        var newer = NewBackup(owner, Epoch.AddDays(1), sizeBytes: 10);
        await SeedBackupsAsync(factory, older, newer);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byDefault = await GetJsonAsync(admin, "/api/v1/backups");
        using var startedAt = await GetJsonAsync(admin, "/api/v1/backups?sort=startedAt");
        using var bySize = await GetJsonAsync(admin, "/api/v1/backups?sort=size");
        using var bySizeDescending = await GetJsonAsync(admin, "/api/v1/backups?sort=-size");
        using var beyond = await GetJsonAsync(admin, "/api/v1/backups?page=3&pageSize=10");

        Assert.Equal([newer.Id, older.Id], Ids(byDefault));
        Assert.Equal([older.Id, newer.Id], Ids(startedAt));
        Assert.Equal([newer.Id, older.Id], Ids(bySize));
        Assert.Equal([older.Id, newer.Id], Ids(bySizeDescending));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, beyond.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.RootElement.GetProperty("page").GetInt32());
    }

    /// <summary>A bound the history refuses answers 400 with its code.</summary>
    /// <param name="queryString">The refused query string.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("sort=sha256", "ListSortUnknown")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "BackupDateRangeInverted")]
    public async Task A_bound_the_history_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/backups?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>
    /// The destinations list is on the same contract: a name search matches literally, the kind filter
    /// binds, and an unknown sort or an unoffered size is refused with its code.
    /// </summary>
    [Fact]
    public async Task The_destinations_list_searches_filters_and_refuses_on_the_same_contract()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using (var scope = factory.Services.CreateScope())
        {
            var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            backups.BackupDestinations.AddRange(
                new BackupDestination(
                    Guid.NewGuid(), "Disk one", BackupDestinationKind.Local, string.Empty, isDefault: false, Epoch),
                new BackupDestination(
                    Guid.NewGuid(), "Off_site", BackupDestinationKind.S3, string.Empty, isDefault: false, Epoch));
            await backups.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var all = await GetJsonAsync(admin, "/api/v1/backup-destinations");
        using var byName = await GetJsonAsync(admin, "/api/v1/backup-destinations?search=OFF_");
        using var wildcard = await GetJsonAsync(admin, "/api/v1/backup-destinations?search=" + Uri.EscapeDataString("Disk%"));
        using var byKind = await GetJsonAsync(admin, "/api/v1/backup-destinations?kind=s3");
        var unknownSort = await admin.GetAsync("/api/v1/backup-destinations?sort=path");
        var unofferedSize = await admin.GetAsync("/api/v1/backup-destinations?pageSize=7");

        Assert.Equal(2, all.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("Off_site", Assert.Single(Items(byName)).GetProperty("name").GetString());
        Assert.Equal(0, wildcard.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("s3", Assert.Single(Items(byKind)).GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, unknownSort.StatusCode);
        Assert.Equal("ListSortUnknown", await CodeOfAsync(unknownSort));
        Assert.Equal(HttpStatusCode.BadRequest, unofferedSize.StatusCode);
        Assert.Equal("ListPageSizeNotOffered", await CodeOfAsync(unofferedSize));
    }

    /// <summary>
    /// The attention answer counts accounts whose latest finished run failed: a failure a later
    /// completed run superseded is not counted, a running row neither raises nor clears one, and a
    /// customer is told only about their own account.
    /// </summary>
    [Fact]
    public async Task The_attention_answer_counts_unrecovered_accounts_within_the_tenant_scope()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);

        var superseded = new Backup(Guid.NewGuid(), own, null, BackupKind.Manual, Epoch);
        superseded.Failed("AgentSystemFailure", Epoch);
        var recovery = NewBackup(own, Epoch.AddDays(1));
        var strangerFailure = new Backup(Guid.NewGuid(), stranger, null, BackupKind.Manual, Epoch.AddDays(2));
        strangerFailure.Failed("AgentSystemFailure", Epoch.AddDays(2));
        var strangerRunning = new Backup(Guid.NewGuid(), stranger, null, BackupKind.Manual, Epoch.AddDays(3));
        await SeedBackupsAsync(factory, superseded, recovery, strangerFailure, strangerRunning);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminView = await GetJsonAsync(admin, "/api/v1/backups/attention");
        using var customerView = await GetJsonAsync(customer, "/api/v1/backups/attention");
        using var customerNamingStranger = await GetJsonAsync(customer, $"/api/v1/backups/attention?accountId={stranger}");

        Assert.Equal(1, adminView.RootElement.GetProperty("unrecoveredAccounts").GetInt32());
        Assert.Equal(strangerFailure.Id, adminView.RootElement.GetProperty("newest").GetProperty("id").GetGuid());
        Assert.Equal("stranger", adminView.RootElement.GetProperty("newest").GetProperty("ownerName").GetString());
        Assert.Equal(0, customerView.RootElement.GetProperty("unrecoveredAccounts").GetInt32());
        Assert.Equal(JsonValueKind.Null, customerView.RootElement.GetProperty("newest").ValueKind);
        Assert.Equal(0, customerNamingStranger.RootElement.GetProperty("unrecoveredAccounts").GetInt32());
    }

    /// <summary>Migrates, seeds the administrator, and stores one account, returning its id.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to store.</param>
    private static async Task<Guid> SeedWorldAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        return accountId;
    }

    /// <summary>Stores the given backup rows.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="rows">The rows.</param>
    private static async Task SeedBackupsAsync(WebApplicationFactory<Program> factory, params Backup[] rows)
    {
        // Written through a context resolved outside a request, so no tenant filter separates the
        // seed: the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        backups.Backups.AddRange(rows);
        await backups.SaveChangesAsync();
    }

    /// <summary>Builds one completed manual backup row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="startedAt">When the run began.</param>
    /// <param name="sizeBytes">The artifact's size.</param>
    private static Backup NewBackup(Guid accountId, DateTimeOffset startedAt, long sizeBytes = 4096)
    {
        var backup = new Backup(Guid.NewGuid(), accountId, destinationId: null, BackupKind.Manual, startedAt);
        backup.Completed(sizeBytes, new string('a', 64), 1, startedAt);
        return backup;
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The rows on a page.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<JsonElement> Items(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>The ids on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<Guid> Ids(JsonDocument page)
    {
        return Items(page)
            .Select(item => { return item.GetProperty("id").GetGuid(); })
            .ToList();
    }

    /// <summary>Reads the machine code of a problem answer.</summary>
    /// <param name="response">The refused response.</param>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }
}
