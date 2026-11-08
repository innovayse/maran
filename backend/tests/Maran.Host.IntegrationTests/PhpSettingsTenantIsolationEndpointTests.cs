using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The tenant boundary of <c>GET</c> and <c>PUT /api/v1/php-settings</c> (issue #138) over real HTTP
/// against a real PostgreSQL: customer B cannot read or replace customer A's settings, whatever the
/// request says.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint takes no account id from the caller, so the boundary is that the account is always the
/// caller's own. Each test therefore asserts both halves: B is answered about B's account only, and A's
/// stored rows are the same afterwards. A control reads A's own settings as A, so an answer that was empty
/// for everybody could not pass.
/// </para>
/// <para>
/// The agent is replaced by one that throws on any call. Neither customer has a PHP site, so a request
/// that reached a pool would fail loudly instead of quietly measuring something else.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PhpSettingsTenantIsolationEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PhpSettingsTenantIsolationEndpointTests(PostgresFixture postgres)
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

    /// <summary>Customer B reads B's own settings and never A's, while A reads A's.</summary>
    [Fact]
    public async Task A_customer_reads_only_their_own_php_settings()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg, UseThrowingAgent);
        var (accountA, accountB) = await SeedTwoCustomersAsync(factory);
        await StoreAsync(factory, accountA, "memory_limit", "128M");
        using var customerA = await EndpointHarness.SignInAsync(factory, "customer-a");
        using var customerB = await EndpointHarness.SignInAsync(factory, "customer-b");

        using var own = JsonDocument.Parse(await customerA.GetStringAsync("/api/v1/php-settings"));
        using var foreign = JsonDocument.Parse(await customerB.GetStringAsync("/api/v1/php-settings"));

        // The control: the same endpoint shows A's value to A, so B's empty answer is not an empty endpoint.
        Assert.Equal(accountA, own.RootElement.GetProperty("accountId").GetGuid());
        Assert.Equal("128", ValueOf(own, "memory_limit"));
        Assert.Equal(accountB, foreign.RootElement.GetProperty("accountId").GetGuid());
        Assert.Null(ValueOf(foreign, "memory_limit"));
    }

    /// <summary>
    /// Customer B's replacement changes B's settings only, even when the body names A's account.
    /// </summary>
    [Fact]
    public async Task A_customer_replacing_settings_changes_only_their_own_account()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg, UseThrowingAgent);
        var (accountA, accountB) = await SeedTwoCustomersAsync(factory);
        await StoreAsync(factory, accountA, "memory_limit", "128M");
        using var customerB = await EndpointHarness.SignInAsync(factory, "customer-b");

        using var response = await customerB.PutAsJsonAsync(
            "/api/v1/php-settings",
            new
            {
                accountId = accountA,
                settings = new[] { new { name = "memory_limit", value = "64" } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(accountB, body.RootElement.GetProperty("accountId").GetGuid());
        Assert.Equal("128M", Assert.Single(await RowsOfAsync(factory, accountA)).Value);
        Assert.Equal("64M", Assert.Single(await RowsOfAsync(factory, accountB)).Value);
    }

    /// <summary>The value of one setting in a settings answer, or null when it is not set.</summary>
    /// <param name="answer">The parsed answer.</param>
    /// <param name="name">The setting's name.</param>
    /// <returns>The value in the wire spelling, or null.</returns>
    private static string? ValueOf(JsonDocument answer, string name)
    {
        var item = answer.RootElement.GetProperty("items").EnumerateArray().Single(candidate =>
        {
            return candidate.GetProperty("name").GetString() == name;
        });

        // A null may be written or left out, depending on the host's serializer: both mean "not set".
        return item.TryGetProperty("value", out var value) ? value.GetString() : null;
    }

    /// <summary>Replaces the agent's PHP client with one that fails on any call.</summary>
    /// <param name="services">The host's service collection.</param>
    private static void UseThrowingAgent(IServiceCollection services)
    {
        services.AddSingleton<IAgentPhpClient>(new ThrowingAgentPhpClient());
    }

    /// <summary>Creates the schemas and two customers, each owning an account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The two accounts' ids.</returns>
    private static async Task<(Guid AccountA, Guid AccountB)> SeedTwoCustomersAsync(
        WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountA) = await EndpointHarness.SeedAccountAsync(factory, "acct-a");
        var (_, accountB) = await EndpointHarness.SeedAccountAsync(factory, "acct-b");
        await EndpointHarness.SeedCustomerAsync(factory, "customer-a", accountA);
        await EndpointHarness.SeedCustomerAsync(factory, "customer-b", accountB);

        return (accountA, accountB);
    }

    /// <summary>Stores one setting for an account, outside any request so no tenant filter separates the seed.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="name">The setting's name.</param>
    /// <param name="value">The value in php.ini's spelling.</param>
    private static async Task StoreAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string name,
        string value)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        sites.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), accountId, name, value));
        await sites.SaveChangesAsync();
    }

    /// <summary>Reads the stored settings of one account, bypassing the tenant filter the way a seed does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The account's rows.</returns>
    private static async Task<IReadOnlyList<PhpSetting>> RowsOfAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        return await sites.PhpSettings
            .IgnoreQueryFilters()
            .Where(setting => setting.AccountId == accountId)
            .ToListAsync();
    }
}
