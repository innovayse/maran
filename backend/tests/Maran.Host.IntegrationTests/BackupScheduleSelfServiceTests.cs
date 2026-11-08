using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Policies;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A customer's own backup schedule (<c>/api/v1/me/backup-schedule</c>, issue #147) over real HTTP
/// against real PostgreSQL: that it is the caller's and nobody else's, that it can never become the
/// host-wide policy or aim at a destination, and that the administrator's endpoint stays closed to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The route names no account, so there is no identifier to substitute.</b> The tenant-isolation
/// case is therefore measured three ways: a body that names another account changes nothing but the
/// caller's own row, a caller with no account of their own (an administrator) is answered 404 and
/// never 403, and a customer reads only their own schedule although another account's is in the table.
/// </para>
/// <para>
/// This is the isolation class named for the two routes in the customer self-service matrix
/// (<c>Baselines/customer-operations.txt</c>).
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupScheduleSelfServiceTests : IAsyncLifetime
{
    /// <summary>The customer-reachable route under test.</summary>
    private const string Route = "/api/v1/me/backup-schedule";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupScheduleSelfServiceTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused on both routes.</summary>
    /// <param name="method">The HTTP method.</param>
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task An_anonymous_caller_is_refused(string method)
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer with no schedule reads an empty scope, saves one, and reads it back.</summary>
    /// <remarks>
    /// The inverse control for every refusal below: without a working round trip, "answered 404" would
    /// also be what a broken route or an unmigrated schema says to everybody.
    /// </remarks>
    [Fact]
    public async Task A_customer_saves_and_reads_back_their_own_schedule()
    {
        await using var factory = await BootAsync();
        var world = await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var empty = await alpha.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(empty)).GetProperty("schedule").ValueKind);

        var saved = await alpha.PutAsJsonAsync(Route, Body());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = await JsonAsync(saved);
        Assert.Equal(world.AlphaId, dto.GetProperty("accountId").GetGuid());
        Assert.Equal("weekly", dto.GetProperty("frequency").GetString());
        Assert.Equal("sunday", dto.GetProperty("dayOfWeekUtc").GetString());
        Assert.Equal(5, dto.GetProperty("retainCount").GetInt32());
        Assert.True(dto.GetProperty("enabled").GetBoolean());

        var read = await JsonAsync(await alpha.GetAsync(Route));
        Assert.Equal(dto.GetProperty("id").GetGuid(), read.GetProperty("schedule").GetProperty("id").GetGuid());
    }

    /// <summary>A body naming another account's id changes nothing but the caller's own row.</summary>
    [Fact]
    public async Task A_body_naming_another_account_cannot_aim_the_schedule_at_it()
    {
        await using var factory = await BootAsync();
        var world = await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var response = await alpha.PutAsJsonAsync(Route, new
        {
            frequency = "daily",
            hourUtc = 4,
            dayOfWeekUtc = (string?)null,
            retainCount = 3,
            enabled = true,
            accountId = world.BetaId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await RowsAsync(factory);
        var row = Assert.Single(rows);
        Assert.Equal(world.AlphaId, row.AccountId);
        Assert.DoesNotContain(rows, stored => { return stored.AccountId == world.BetaId; });
    }

    /// <summary>A body naming a destination is ignored: the row keeps the default, and a customer never reaches the host-wide policy.</summary>
    [Fact]
    public async Task A_customer_can_neither_name_a_destination_nor_write_the_host_wide_policy()
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var response = await alpha.PutAsJsonAsync(Route, new
        {
            frequency = "daily",
            hourUtc = 4,
            dayOfWeekUtc = (string?)null,
            retainCount = 3,
            enabled = true,
            destinationId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await RowsAsync(factory);
        Assert.All(rows, row =>
        {
            Assert.Null(row.DestinationId);
            Assert.NotNull(row.AccountId);
        });
    }

    /// <summary>A customer reads only their own schedule although another account's is stored.</summary>
    [Fact]
    public async Task A_customer_never_reads_another_accounts_schedule()
    {
        await using var factory = await BootAsync();
        var world = await SeedAsync(factory);
        await StoreAsync(factory, world.BetaId, retainCount: 6);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");
        using var beta = await EndpointHarness.SignInAsync(factory, "beta-owner");

        var fromAlpha = await JsonAsync(await alpha.GetAsync(Route));
        var fromBeta = await JsonAsync(await beta.GetAsync(Route));

        Assert.Equal(JsonValueKind.Null, fromAlpha.GetProperty("schedule").ValueKind);
        Assert.Equal(world.BetaId, fromBeta.GetProperty("schedule").GetProperty("accountId").GetGuid());
    }

    /// <summary>
    /// A caller with no account of their own, an administrator, is answered 404 and never 403, and the
    /// host-wide policy they could otherwise see is not shown.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task A_caller_with_no_account_is_answered_not_found_and_never_forbidden(string method)
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        await StoreAsync(factory, accountId: null, retainCount: 30);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await SendAsync(admin, method, Body());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        var rows = await RowsAsync(factory);
        var hostWide = Assert.Single(rows);
        Assert.Null(hostWide.AccountId);
        Assert.Equal(30, hostWide.RetainCount);
    }

    /// <summary>The customer's bounds are enforced: more copies than the ceiling is a 400 and writes nothing.</summary>
    [Fact]
    public async Task More_copies_than_the_customer_ceiling_is_refused_and_nothing_is_written()
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var response = await alpha.PutAsJsonAsync(Route, Body() with { RetainCount = CustomerBackupLimits.MaximumRetainCount + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("BackupScheduleCustomerRetainTooHigh", await ProblemDocument.CodeAsync(response));
        Assert.Empty(await RowsAsync(factory));
    }

    /// <summary>An operator viewing as the customer may read the schedule but not change it.</summary>
    [Fact]
    public async Task A_view_as_session_may_read_but_not_change_the_schedule()
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "alpha");

        var read = await viewing.GetAsync(Route);
        var refused = await viewing.PutAsJsonAsync(Route, Body());

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("HostViewAsNotAllowed", await ProblemDocument.CodeAsync(refused));
        Assert.Empty(await RowsAsync(factory));
    }

    /// <summary>A save is journalled under the customer's own name and their account.</summary>
    [Fact]
    public async Task A_save_is_journalled_under_the_customers_name()
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var response = await alpha.PutAsJsonAsync(Route, Body());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = Assert.Single(await SubUserTeamWorld.AuditEntriesAsync(factory, BackupAuditActions.BackupScheduleSaved));
        Assert.Equal("alpha-owner", entry.ActorUsername);
        Assert.Equal("alpha", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The administrator's schedule endpoints still answer a customer 403, untouched by the customer's.</summary>
    /// <param name="method">The HTTP method.</param>
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task The_administrator_schedule_endpoints_stay_closed_to_a_customer(string method)
    {
        await using var factory = await BootAsync();
        await SeedAsync(factory);
        using var alpha = await EndpointHarness.SignInAsync(factory, "alpha-owner");

        var response = await SendAsync(alpha, method, Body(), "/api/v1/backup-schedules");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A valid weekly schedule body.</summary>
    /// <returns>Sundays at 03:00, keeping five copies.</returns>
    private static ScheduleBody Body()
    {
        return new ScheduleBody("weekly", 3, "sunday", 5, true);
    }

    /// <summary>Sends a request with the schedule body where the method carries one.</summary>
    /// <param name="client">The caller.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="body">The body for a PUT.</param>
    /// <param name="route">The route; the customer's by default.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        ScheduleBody body,
        string route = Route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Reads a response body as JSON.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The root element, cloned so it outlives the document.</returns>
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Boots the panel with the schemas this test reads.</summary>
    /// <returns>The booted host.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
        return factory;
    }

    /// <summary>Seeds an administrator and two accounts, each with its owner.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The two account ids.</returns>
    private static async Task<World> SeedAsync(WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, alpha) = await EndpointHarness.SeedAccountAsync(factory, "alpha");
        var (_, beta) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "alpha-owner", alpha);
        await EndpointHarness.SeedCustomerAsync(factory, "beta-owner", beta);
        return new World(alpha, beta);
    }

    /// <summary>Stores a schedule row directly, outside any request, so the filter under test is the only separator.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account, or <c>null</c> for the host-wide policy.</param>
    /// <param name="retainCount">How many copies it keeps.</param>
    private static async Task StoreAsync(WebApplicationFactory<Program> factory, Guid? accountId, int retainCount)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        backups.BackupSchedules.Add(new BackupSchedule(
            Guid.NewGuid(), accountId, destinationId: null, new DailyBackupCadence(1), retainCount));
        await backups.SaveChangesAsync();
    }

    /// <summary>Reads every stored schedule, past the tenant filter.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Every row.</returns>
    private static async Task<List<BackupSchedule>> RowsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        return await backups.BackupSchedules.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    /// <summary>The wire body of a schedule save, as the SPA sends it.</summary>
    /// <param name="Frequency">The cadence name.</param>
    /// <param name="HourUtc">The hour, in UTC.</param>
    /// <param name="DayOfWeekUtc">The weekday name, or <c>null</c> for a daily schedule.</param>
    /// <param name="RetainCount">How many copies to keep.</param>
    /// <param name="Enabled">Whether the schedule runs.</param>
    private sealed record ScheduleBody(string Frequency, int HourUtc, string? DayOfWeekUtc, int RetainCount, bool Enabled);

    /// <summary>The two accounts the test world holds.</summary>
    /// <param name="AlphaId">The first account.</param>
    /// <param name="BetaId">The second account.</param>
    private sealed record World(Guid AlphaId, Guid BetaId);
}
