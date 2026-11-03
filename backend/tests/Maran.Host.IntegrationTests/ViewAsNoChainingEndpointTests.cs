using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// View-as is ONE hop per token (issues #317, #179, decision D1): no token a view-as produced can start
/// another view of either kind, a customer and an anonymous caller start neither, and the refusals leave
/// no session and no journal entry behind. Threat note:
/// docs/superpowers/notes/2026-11-03-reseller-role-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ViewAsNoChainingEndpointTests : IAsyncLifetime
{
    /// <summary>The endpoint that starts an account view.</summary>
    private const string AccountEndpoint = "/api/v1/view-as";

    /// <summary>The endpoint that starts a reseller view.</summary>
    private const string ResellerEndpoint = "/api/v1/view-as/reseller";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ViewAsNoChainingEndpointTests(PostgresFixture postgres)
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
    /// Every kind of view-as token, asked to start every kind of view, is refused with the view-as code and
    /// starts nothing: the one session each token came from is the only one that exists afterwards.
    /// </summary>
    [Fact]
    public async Task No_view_as_token_can_start_another_view_of_either_kind()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var adminAsReseller = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);
        using var adminAsAccount = await IdentityResellerWorld.StartViewingAccountAsync(factory, admin, world.Acme);
        using var resellerAsAccount = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);
        var started = await IdentityResellerWorld.ViewAsSessionCountAsync(factory);
        var journalled = (await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted)).Count;

        var wrong = new List<string>();
        foreach (var (kind, token) in new[]
        {
            ("administrator as a reseller", adminAsReseller),
            ("administrator as an account", adminAsAccount),
            ("reseller as an account", resellerAsAccount),
        })
        {
            foreach (var (endpoint, body) in new (string, object)[]
            {
                (AccountEndpoint, new { AccountId = world.Acme }),
                (AccountEndpoint, new { AccountId = world.Initech }),
                (ResellerEndpoint, new { ResellerId = world.Nova.ResellerId }),
                (ResellerEndpoint, new { ResellerId = world.Orbit.ResellerId }),
            })
            {
                var response = await token.PostAsJsonAsync(endpoint, body);
                var code = response.StatusCode == HttpStatusCode.Forbidden ? await CodeOfAsync(response) : null;
                if (code != "HostViewAsNotAllowed")
                {
                    wrong.Add($"{kind} POST {endpoint} answered {(int)response.StatusCode} {code}");
                }
            }
        }

        Assert.Equal(3, started);
        Assert.Empty(wrong);
        Assert.Equal(started, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        Assert.Equal(
            journalled,
            (await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted)).Count);
    }

    /// <summary>A customer is refused both starts by policy (403), and nothing is issued or journalled.</summary>
    [Fact]
    public async Task A_customer_cannot_start_either_kind()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var olive = await EndpointHarness.SignInAsync(factory, "olive");

        var account = await olive.PostAsJsonAsync(AccountEndpoint, new { AccountId = world.Acme });
        var reseller = await olive.PostAsJsonAsync(ResellerEndpoint, new { ResellerId = world.Nova.ResellerId });

        Assert.Equal(HttpStatusCode.Forbidden, account.StatusCode);
        Assert.Equal("HostForbidden", await CodeOfAsync(account));
        Assert.Equal(HttpStatusCode.Forbidden, reseller.StatusCode);
        Assert.Equal("HostForbidden", await CodeOfAsync(reseller));
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        Assert.Empty(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
    }

    /// <summary>An anonymous caller is refused both starts (401), and nothing is issued.</summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_start_either_kind()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var anonymous = factory.CreateClient();

        var account = await anonymous.PostAsJsonAsync(AccountEndpoint, new { AccountId = world.Acme });
        var reseller = await anonymous.PostAsJsonAsync(ResellerEndpoint, new { ResellerId = world.Nova.ResellerId });

        Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reseller.StatusCode);
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
    }

    /// <summary>Reads the machine code out of a problem answer.</summary>
    /// <param name="response">The refused answer.</param>
    /// <returns>Its <c>code</c>.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Boots the host with the view-as world: an administrator, two resellers and their accounts.</summary>
    /// <returns>The booted factory, which the caller disposes, and the identities the tests address.</returns>
    private async Task<(WebApplicationFactory<Program> Factory, ViewAsWorld World)> BootAsync()
    {
        var factory = await IdentityResellerWorld.BootAsync(_pg);

        return (factory, await IdentityResellerWorld.SeedViewAsWorldAsync(factory));
    }
}
