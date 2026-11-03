using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/view-as</c> for the two operators that may start an account view (issues #317, #314,
/// #179): an administrator for any account, and a reseller for one of ITS OWN. Whatever a reseller names
/// that is not its own answers 404 exactly as an account that does not exist does, and no start from a
/// reseller ever reaches a reseller, an administrator or another reseller's account. Threat note:
/// docs/superpowers/notes/2026-11-03-reseller-role-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ViewAsResellerOperatorEndpointTests : IAsyncLifetime
{
    /// <summary>The endpoint that starts an account view, and reports and ends any view.</summary>
    private const string Endpoint = "/api/v1/view-as";

    /// <summary>The endpoint that starts a reseller view.</summary>
    private const string ResellerEndpoint = "/api/v1/view-as/reseller";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ViewAsResellerOperatorEndpointTests(PostgresFixture postgres)
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
    /// A reseller starts a view of its own account: the token is a customer token for the owning login,
    /// carrying the reseller as actor, and it reads exactly that account.
    /// </summary>
    [Fact]
    public async Task A_reseller_views_as_the_owner_of_its_own_account()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");

        var response = await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Acme });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        Assert.Equal(nameof(UserRole.Customer), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(world.Acme.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
        Assert.Equal("ViewAs", token.GetClaim(PanelClaimTypes.SessionKind).Value);
        Assert.False(token.TryGetClaim(PanelClaimTypes.ResellerId, out _));
        using (var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value))
        {
            Assert.Equal("nova", actor.RootElement.GetProperty(PanelClaimTypes.Username).GetString());
            Assert.Equal(world.Nova.UserId.ToString(), actor.RootElement.GetProperty(PanelClaimTypes.UserId).GetString());
        }

        var viewing = body.RootElement.GetProperty("viewing");
        Assert.Equal("account", viewing.GetProperty("kind").GetString());
        Assert.Equal(world.Acme, viewing.GetProperty("subjectId").GetGuid());
        Assert.Equal("acme", viewing.GetProperty("subjectName").GetString());
        Assert.Equal("olive", viewing.GetProperty("username").GetString());
        Assert.Equal("nova", viewing.GetProperty("operatorUsername").GetString());

        using var viewingClient = IdentityResellerWorld.ClientWith(factory, accessToken);
        using var account = JsonDocument.Parse(await viewingClient.GetStringAsync("/api/v1/accounts/me"));
        Assert.Equal("acme.example.com", account.RootElement.GetProperty("primaryDomain").GetString());
        using var state = JsonDocument.Parse(await viewingClient.GetStringAsync(Endpoint));
        Assert.Equal("account", state.RootElement.GetProperty("viewing").GetProperty("kind").GetString());
        Assert.Equal("nova", state.RootElement.GetProperty("viewing").GetProperty("operatorUsername").GetString());
    }

    /// <summary>An administrator views as the owner of any account, a reseller's and its own alike.</summary>
    [Fact]
    public async Task An_administrator_views_as_the_owner_of_any_account_including_a_resellers()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        foreach (var (account, name, owner) in new[] { (world.Acme, "acme", "olive"), (world.Initech, "initech", "ivan") })
        {
            var response = await admin.PostAsJsonAsync(Endpoint, new { AccountId = account });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var token = new JsonWebTokenHandler().ReadJsonWebToken(body.RootElement.GetProperty("accessToken").GetString()!);
            Assert.Equal(nameof(UserRole.Customer), token.GetClaim(PanelClaimTypes.Role).Value);
            Assert.Equal(account.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
            var viewing = body.RootElement.GetProperty("viewing");
            Assert.Equal("account", viewing.GetProperty("kind").GetString());
            Assert.Equal(name, viewing.GetProperty("subjectName").GetString());
            Assert.Equal(owner, viewing.GetProperty("username").GetString());
            Assert.Equal("admin", viewing.GetProperty("operatorUsername").GetString());
        }
    }

    /// <summary>
    /// An account another reseller owns, an administrator's, and one that does not exist are the same
    /// answer: the status, the code and the text, so the reply is no oracle for which accounts exist.
    /// Every refusal is journalled under the reseller, and nothing is issued.
    /// </summary>
    [Fact]
    public async Task A_reseller_naming_an_account_it_does_not_own_is_answered_like_one_that_does_not_exist()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        var nobody = Guid.NewGuid();

        var foreign = await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Globex });
        var administrators = await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Initech });
        var absent = await nova.PostAsJsonAsync(Endpoint, new { AccountId = nobody });

        foreach (var response in new[] { foreign, administrators, absent })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("ViewAsAccountNotFound", await CodeOfAsync(response));
        }

        Assert.Equal(await TitleOfAsync(absent), await TitleOfAsync(foreign));
        Assert.Equal(await TitleOfAsync(absent), await TitleOfAsync(administrators));
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        var entries = await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted);
        Assert.Equal(
            [world.Globex.ToString(), world.Initech.ToString(), nobody.ToString()],
            entries.Select(entry => { return entry.Subject; }).ToArray());
        Assert.All(entries, entry =>
        {
            Assert.False(entry.Succeeded);
            Assert.Equal(world.Nova.UserId, entry.ActorUserId);
        });
    }

    /// <summary>
    /// The one distinguishable refusal is for an account the reseller already owns: its owner login may not
    /// sign in (409). The same condition on another reseller's account is still the plain 404.
    /// </summary>
    [Fact]
    public async Task The_login_unavailable_conflict_is_reachable_only_for_an_account_the_reseller_owns()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            foreach (var login in await identity.Users.Where(u => u.Username == "olive" || u.Username == "gus").ToListAsync())
            {
                login.Suspend();
            }

            await identity.SaveChangesAsync();
        }

        using var nova = await EndpointHarness.SignInAsync(factory, "nova");

        var own = await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Acme });
        var foreign = await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Globex });

        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("ViewAsLoginUnavailable", await CodeOfAsync(own));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("ViewAsAccountNotFound", await CodeOfAsync(foreign));
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
    }

    /// <summary>A reseller may not start a reseller view, of another reseller or of itself: 403 by policy, nothing issued.</summary>
    [Fact]
    public async Task A_reseller_cannot_start_a_reseller_view()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");

        foreach (var resellerId in new[] { world.Orbit.ResellerId, world.Nova.ResellerId })
        {
            var response = await nova.PostAsJsonAsync(ResellerEndpoint, new { ResellerId = resellerId });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("HostForbidden", await CodeOfAsync(response));
        }

        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        Assert.Empty(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
    }

    /// <summary>
    /// There is no edge to an administrator: no start names one, whoever asks, so an administrator's
    /// login id answers 404 on both endpoints and nothing is issued.
    /// </summary>
    [Fact]
    public async Task Nobody_can_view_as_an_administrator()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        var adminId = await IdentityResellerWorld.UserIdAsync(factory, "admin");

        var asReseller = await admin.PostAsJsonAsync(ResellerEndpoint, new { ResellerId = adminId });
        var asAccountByAdmin = await admin.PostAsJsonAsync(Endpoint, new { AccountId = adminId });
        var asAccountByReseller = await nova.PostAsJsonAsync(Endpoint, new { AccountId = adminId });

        Assert.Equal(HttpStatusCode.NotFound, asReseller.StatusCode);
        Assert.Equal("ViewAsResellerNotFound", await CodeOfAsync(asReseller));
        Assert.Equal(HttpStatusCode.NotFound, asAccountByAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asAccountByReseller.StatusCode);
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));

        // The inverse control: the same administrator and reseller do start the views that ARE edges.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(ResellerEndpoint, new { ResellerId = world.Nova.ResellerId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nova.PostAsJsonAsync(Endpoint, new { AccountId = world.Acme })).StatusCode);
    }

    /// <summary>
    /// The customer token a reseller starts is a customer's: it reaches neither the reseller's own screens
    /// nor an administrator endpoint, and it is refused every write the view-as allow-list does not declare safe.
    /// </summary>
    [Fact]
    public async Task The_token_a_reseller_starts_is_a_customers_and_reads_and_writes_as_one()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);

        var wrong = new List<string>();
        foreach (var path in new[] { "/api/v1/reseller/me", "/api/v1/accounts", "/api/v1/resellers", "/api/v1/plans" })
        {
            var response = await viewing.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                wrong.Add($"GET {path} answered {(int)response.StatusCode}");
            }
        }

        foreach (var path in new[] { "/api/v1/sites", "/api/v1/databases", "/api/v1/sftp-users" })
        {
            var response = await viewing.PostAsJsonAsync(path, new { });
            var code = response.StatusCode == HttpStatusCode.Forbidden ? await CodeOfAsync(response) : null;
            if (code != "HostViewAsNotAllowed")
            {
                wrong.Add($"POST {path} answered {(int)response.StatusCode} {code}");
            }
        }

        Assert.Empty(wrong);
        Assert.Equal(HttpStatusCode.OK, (await viewing.GetAsync("/api/v1/accounts/me")).StatusCode);
    }

    /// <summary>The start and the end are journalled under the reseller, naming the account and its login; the end carries the impersonator.</summary>
    [Fact]
    public async Task Start_and_end_by_a_reseller_are_journalled_with_the_reseller_and_the_viewed_account()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);

        var ended = await viewing.DeleteAsync(Endpoint);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        var started = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
        var stopped = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsEnded));
        Assert.Equal(world.Nova.UserId, started.ActorUserId);
        Assert.Equal("nova", started.ActorUsername);
        Assert.Equal("account acme as olive", started.Subject);
        Assert.True(started.Succeeded);
        Assert.Null(started.Impersonator);
        Assert.Equal(world.Nova.UserId, stopped.ActorUserId);
        Assert.Equal("account acme as olive", stopped.Subject);
        Assert.Equal(world.Nova.UserId, stopped.Impersonator?.OperatorId);
        Assert.Equal("nova", stopped.Impersonator?.Username);
    }

    /// <summary>Back returns the reseller to its own session: the view token dies and its cookie yields a reseller token.</summary>
    [Fact]
    public async Task Back_returns_the_reseller_to_its_own_session()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await IdentityResellerWorld.SignInWithCookieAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);

        Assert.Equal(HttpStatusCode.NoContent, (await viewing.DeleteAsync(Endpoint)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync("/api/v1/accounts/me")).StatusCode);
        var refreshed = await nova.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var body = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        var own = new JsonWebTokenHandler().ReadJsonWebToken(body.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(nameof(UserRole.Reseller), own.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(world.Nova.ResellerId.ToString(), own.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.False(own.TryGetClaim(PanelClaimTypes.SessionKind, out _));
    }

    /// <summary>Signing the reseller out everywhere ends the account view it started.</summary>
    [Fact]
    public async Task Signing_the_reseller_out_everywhere_ends_the_account_view()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var nova = await EndpointHarness.SignInAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, nova, world.Acme);
        Assert.Equal(HttpStatusCode.OK, (await viewing.GetAsync("/api/v1/accounts/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await nova.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync("/api/v1/accounts/me")).StatusCode);
    }

    /// <summary>Reads the machine code out of a problem answer.</summary>
    /// <param name="response">The refused answer.</param>
    /// <returns>Its <c>code</c>.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Reads the sentence out of a problem answer.</summary>
    /// <param name="response">The refused answer.</param>
    /// <returns>Its <c>title</c>.</returns>
    private static async Task<string?> TitleOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("title").GetString();
    }

    /// <summary>Boots the host with the view-as world: an administrator, two resellers and their accounts.</summary>
    /// <returns>The booted factory, which the caller disposes, and the identities the tests address.</returns>
    private async Task<(WebApplicationFactory<Program> Factory, ViewAsWorld World)> BootAsync()
    {
        var factory = await IdentityResellerWorld.BootAsync(_pg);

        return (factory, await IdentityResellerWorld.SeedViewAsWorldAsync(factory));
    }
}
