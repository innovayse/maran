using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/me/accounts</c> and <c>POST /api/v1/me/active-account</c> over real HTTP: a person
/// who owns two accounts switches between them without signing out, the switch is journalled, the
/// tenant data follows the account the new token names, and an account the person does not own
/// answers 404 exactly like one that does not exist.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ActiveAccountEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The audit writer every host in this class journals into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ActiveAccountEndpointTests(PostgresFixture postgres)
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

    /// <summary>The list names both accounts of a person who owns two and marks the oldest as active.</summary>
    [Fact]
    public async Task A_person_who_owns_two_accounts_lists_both_with_the_oldest_active()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "olive");

        var rows = await ReadAccountsAsync(client);

        Assert.Equal(new[] { "acme", "globex" }, rows.Select(row => row.Name));
        Assert.Equal("acme", Assert.Single(rows, row => row.IsActive).Name);
    }

    /// <summary>Switching to the second owned account follows through to the tenant data and is journalled.</summary>
    [Fact]
    public async Task Switching_to_the_second_account_moves_the_tenant_data_with_it_and_is_journalled()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "olive");
        var globex = (await ReadAccountsAsync(client)).Single(row => row.Name == "globex").AccountId;
        Assert.Equal("acme.example.com", await PrimaryDomainAsync(client));

        var response = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = globex });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(globex, body.RootElement.GetProperty("accountId").GetGuid());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString()!);

        // The tenant read follows the account claim of the NEW token, with no sign-out in between.
        Assert.Equal("globex.example.com", await PrimaryDomainAsync(client));
        Assert.Equal("globex", Assert.Single(await ReadAccountsAsync(client), row => row.IsActive).Name);

        var entry = Assert.Single(_audit.Entries, candidate => candidate.Action == AuditActions.ActiveAccountSwitched);
        Assert.Equal(globex.ToString(), entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An account the person does not own answers 404, changes nothing, and is journalled as refused.</summary>
    [Fact]
    public async Task Switching_to_an_account_the_person_does_not_own_answers_404_and_changes_nothing()
    {
        await using var factory = await BootAsync();
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "initech");
        using var client = await EndpointHarness.SignInAsync(factory, "olive");

        var response = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = stranger });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("acme.example.com", await PrimaryDomainAsync(client));
        var entry = Assert.Single(_audit.Entries, candidate => candidate.Action == AuditActions.ActiveAccountSwitched);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account id that exists nowhere answers exactly as one owned by somebody else.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_answers_exactly_as_one_owned_by_somebody_else()
    {
        await using var factory = await BootAsync();
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "initech");
        using var client = await EndpointHarness.SignInAsync(factory, "olive");

        var foreign = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = stranger });
        var unknown = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = Guid.NewGuid() });

        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>An administrator owns no account, so listing answers an empty list and switching answers 404.</summary>
    [Fact]
    public async Task An_administrator_has_no_accounts_to_switch_between()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var (_, anyAccount) = await EndpointHarness.SeedAccountAsync(factory, "initech");

        var rows = await ReadAccountsAsync(admin);
        var switched = await admin.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = anyAccount });

        Assert.Empty(rows);
        Assert.Equal(HttpStatusCode.NotFound, switched.StatusCode);
    }

    /// <summary>An anonymous caller reaches neither endpoint.</summary>
    [Fact]
    public async Task An_anonymous_caller_reaches_neither_endpoint()
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var list = await client.GetAsync("/api/v1/me/accounts");
        var switched = await client.PostAsJsonAsync("/api/v1/me/active-account", new { AccountId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
    }

    /// <summary>Reads the accounts the caller may switch between.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <returns>One row per account.</returns>
    private static async Task<List<AccountRow>> ReadAccountsAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/me/accounts"));

        return body.RootElement.EnumerateArray()
            .Select(row => new AccountRow(
                row.GetProperty("accountId").GetGuid(),
                row.GetProperty("name").GetString()!,
                row.GetProperty("isActive").GetBoolean()))
            .ToList();
    }

    /// <summary>Reads the primary domain of the account the caller's token currently names.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <returns>The domain, which differs per account.</returns>
    private static async Task<string> PrimaryDomainAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/accounts/me"));

        return body.RootElement.GetProperty("primaryDomain").GetString()!;
    }

    /// <summary>Boots the host with an administrator and a customer, olive, who owns acme and globex.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(_audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, globex) = await EndpointHarness.SeedAccountAsync(factory, "globex");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", acme);
        await EndpointHarness.SeedOwnedAccountAsync(factory, "olive", globex);

        return factory;
    }

    /// <summary>One row of the account list, as the endpoint answers it.</summary>
    /// <param name="AccountId">The account's identity.</param>
    /// <param name="Name">The account's name.</param>
    /// <param name="IsActive">Whether the token names this account.</param>
    private sealed record AccountRow(Guid AccountId, string Name, bool IsActive);
}
