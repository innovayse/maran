using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/view-as/reseller</c> over real HTTP and a real journal (issues #317, #314, #179): an
/// administrator views the panel as a reseller's login through a token that carries role Reseller, so it
/// reads what the reseller reads and is refused everything an administrator alone may do and everything
/// that writes; the banner state reports the reseller kind; and every start and end is journalled with
/// both identities. Threat note: docs/superpowers/notes/2026-11-03-reseller-role-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ViewAsResellerEndpointTests : IAsyncLifetime
{
    /// <summary>The endpoint that starts a reseller view.</summary>
    private const string StartEndpoint = "/api/v1/view-as/reseller";

    /// <summary>The endpoint that reports and ends a view.</summary>
    private const string StateEndpoint = "/api/v1/view-as";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The panel clock, moved past the view-as lifetime by the expiry test.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ViewAsResellerEndpointTests(PostgresFixture postgres)
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
    /// Starting answers a reseller token for the reseller's login, carrying the administrator as actor,
    /// naming the reseller and no account, with no cookie, and the banner shape names its kind.
    /// </summary>
    [Fact]
    public async Task Starting_issues_a_reseller_token_carrying_the_administrator_as_actor()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PostAsJsonAsync(StartEndpoint, new { ResellerId = world.Nova.ResellerId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(nameof(UserRole.Reseller), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(world.Nova.ResellerId.ToString(), token.GetClaim(PanelClaimTypes.ResellerId).Value);
        Assert.Equal("ViewAs", token.GetClaim(PanelClaimTypes.SessionKind).Value);
        Assert.False(token.TryGetClaim(PanelClaimTypes.AccountId, out _));
        using (var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value))
        {
            Assert.Equal("admin", actor.RootElement.GetProperty(PanelClaimTypes.Username).GetString());
        }

        var user = body.RootElement.GetProperty("user");
        Assert.Equal("reseller", user.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("accountId").ValueKind);
        var viewing = body.RootElement.GetProperty("viewing");
        Assert.Equal("reseller", viewing.GetProperty("kind").GetString());
        Assert.Equal(world.Nova.ResellerId, viewing.GetProperty("subjectId").GetGuid());
        Assert.Equal("nova", viewing.GetProperty("subjectName").GetString());
        Assert.Equal("nova", viewing.GetProperty("username").GetString());
        Assert.Equal("admin", viewing.GetProperty("operatorUsername").GetString());

        // One field, one meaning: the account-only member the reseller flavour used to share is gone.
        Assert.False(viewing.TryGetProperty("accountId", out _));
        Assert.False(viewing.TryGetProperty("accountName", out _));
    }

    /// <summary>The token reads what the reseller reads: its standing, and its own accounts and nobody else's.</summary>
    [Fact]
    public async Task The_reseller_token_reads_the_resellers_own_standing_and_only_its_own_accounts()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        using var me = JsonDocument.Parse(await viewing.GetStringAsync("/api/v1/reseller/me"));
        using var seen = JsonDocument.Parse(await viewing.GetStringAsync("/api/v1/accounts?pageSize=100"));
        using var all = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/accounts?pageSize=100"));
        var foreign = await viewing.GetAsync($"/api/v1/accounts/{world.Globex}");

        Assert.Equal("nova", me.RootElement.GetProperty("name").GetString());
        Assert.Equal(["acme"], NamesOf(seen));
        Assert.Equal(["acme", "globex", "initech"], NamesOf(all).Order().ToArray());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    /// <summary>
    /// The token carries the VIEWED login's role, so every administrator endpoint refuses it: an
    /// administrator viewing as a reseller keeps no administrator power. The administrator's own token
    /// reads the same endpoints, so the refusal is the token's.
    /// </summary>
    [Fact]
    public async Task An_administrator_viewing_as_a_reseller_keeps_no_administrator_power()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        var refused = new List<string>();
        foreach (var path in new[] { "/api/v1/resellers", "/api/v1/security-policy", "/api/v1/audit" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
            var response = await viewing.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                refused.Add($"GET {path} answered {(int)response.StatusCode}");
            }
        }

        var chained = await viewing.PostAsJsonAsync(StartEndpoint, new { ResellerId = world.Orbit.ResellerId });

        Assert.Empty(refused);
        Assert.Equal(HttpStatusCode.Forbidden, chained.StatusCode);
    }

    /// <summary>
    /// Whatever writes is refused with the view-as code before any handler runs, and nothing changed:
    /// the view-as allow-list covers a token whose role is Reseller exactly as it covers a customer's.
    /// </summary>
    [Fact]
    public async Task The_reseller_token_is_refused_every_write_and_nothing_changes()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        var wrong = new List<string>();
        foreach (var (method, path) in new[]
        {
            ("POST", "/api/v1/accounts"),
            ("POST", "/api/v1/plans"),
            ("POST", $"/api/v1/accounts/{world.Acme}/suspend"),
            ("PUT", $"/api/v1/accounts/{world.Acme}/plan"),
            ("DELETE", $"/api/v1/accounts/{world.Acme}"),
            ("DELETE", $"/api/v1/plans/{Guid.NewGuid()}"),
        })
        {
            var response = await viewing.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = JsonContent.Create(new { }),
            });
            var code = response.StatusCode == HttpStatusCode.Forbidden ? await CodeOfAsync(response) : null;
            if (code != "HostViewAsNotAllowed")
            {
                wrong.Add($"{method} {path} answered {(int)response.StatusCode} {code}");
            }
        }

        Assert.Empty(wrong);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Assert.Equal(3, await accounts.Accounts.CountAsync());
        Assert.Equal(AccountStatus.Active, await accounts.Accounts.Where(a => a.Id == world.Acme).Select(a => a.Status).SingleAsync());
    }

    /// <summary>The state endpoint reports the reseller kind and what the banner shows; an own token reports none.</summary>
    [Fact]
    public async Task The_state_endpoint_reports_the_reseller_kind()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        using var state = JsonDocument.Parse(await viewing.GetStringAsync(StateEndpoint));
        using var own = JsonDocument.Parse(await admin.GetStringAsync(StateEndpoint));

        var banner = state.RootElement.GetProperty("viewing");
        Assert.Equal("reseller", banner.GetProperty("kind").GetString());
        Assert.Equal(world.Nova.ResellerId, banner.GetProperty("subjectId").GetGuid());
        Assert.Equal("nova", banner.GetProperty("subjectName").GetString());
        Assert.Equal("nova", banner.GetProperty("username").GetString());
        Assert.Equal("admin", banner.GetProperty("operatorUsername").GetString());
        Assert.True(banner.GetProperty("expiresAt").GetDateTimeOffset() > _clock.UtcNow);
        Assert.Equal(JsonValueKind.Null, own.RootElement.GetProperty("viewing").ValueKind);
    }

    /// <summary>The start and the end are journalled with the administrator, the reseller viewed, and the impersonator on the end.</summary>
    [Fact]
    public async Task Start_and_end_are_journalled_with_the_administrator_and_the_reseller()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var adminId = await IdentityResellerWorld.UserIdAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        var ended = await viewing.DeleteAsync(StateEndpoint);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        var started = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
        var stopped = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsEnded));
        Assert.Equal(adminId, started.ActorUserId);
        Assert.Equal("admin", started.ActorUsername);
        Assert.Equal("reseller nova as nova", started.Subject);
        Assert.True(started.Succeeded);
        Assert.Null(started.Impersonator);
        Assert.Equal(adminId, stopped.ActorUserId);
        Assert.Equal("reseller nova as nova", stopped.Subject);
        Assert.True(stopped.Succeeded);
        Assert.Equal(adminId, stopped.Impersonator?.OperatorId);
        Assert.Equal("admin", stopped.Impersonator?.Username);
    }

    /// <summary>Back ends the view, the view token is refused at once, and the operator's own cookie returns the operator's own token.</summary>
    [Fact]
    public async Task Back_returns_the_administrator_to_their_own_session()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await IdentityResellerWorld.SignInWithCookieAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);

        Assert.Equal(HttpStatusCode.NoContent, (await viewing.DeleteAsync(StateEndpoint)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync("/api/v1/reseller/me")).StatusCode);
        var refreshed = await admin.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var body = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        var own = new JsonWebTokenHandler().ReadJsonWebToken(body.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(nameof(UserRole.Admin), own.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal("admin", own.GetClaim(PanelClaimTypes.Username).Value);
        Assert.False(own.TryGetClaim(PanelClaimTypes.SessionKind, out _));
        Assert.Equal(1, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
    }

    /// <summary>A reseller that does not exist answers 404 with its code, issues nothing, and the refusal is journalled.</summary>
    [Fact]
    public async Task A_nonexistent_reseller_answers_404_and_is_journalled_as_refused()
    {
        var (factory, _) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var asked = Guid.NewGuid();

        var response = await admin.PostAsJsonAsync(StartEndpoint, new { ResellerId = asked });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ViewAsResellerNotFound", await CodeOfAsync(response));
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        var entry = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
        Assert.False(entry.Succeeded);
        Assert.Equal(asked.ToString(), entry.Subject);
        Assert.Equal("admin", entry.ActorUsername);
    }

    /// <summary>A suspended reseller cannot be viewed as: the answer is the login-unavailable conflict and nothing is issued.</summary>
    [Fact]
    public async Task A_suspended_reseller_cannot_be_viewed_as()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await admin.PostAsync($"/api/v1/resellers/{world.Nova.ResellerId}/suspend", null);

        var response = await admin.PostAsJsonAsync(StartEndpoint, new { ResellerId = world.Nova.ResellerId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ViewAsLoginUnavailable", await CodeOfAsync(response));
        Assert.Equal(0, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        var entry = Assert.Single(await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted));
        Assert.False(entry.Succeeded);
        Assert.Equal("reseller nova as nova", entry.Subject);

        // The inverse control: another reseller, still active, is viewable by the same administrator.
        var other = await admin.PostAsJsonAsync(StartEndpoint, new { ResellerId = world.Orbit.ResellerId });
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    /// <summary>Signing the administrator out everywhere ends the reseller view too.</summary>
    [Fact]
    public async Task Signing_the_administrator_out_everywhere_ends_the_reseller_view()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);
        Assert.Equal(HttpStatusCode.OK, (await viewing.GetAsync("/api/v1/reseller/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync("/api/v1/reseller/me")).StatusCode);
    }

    /// <summary>The reseller view stops working when the session's lifetime passes, without waiting for the token.</summary>
    [Fact]
    public async Task The_reseller_view_stops_working_when_the_session_expires()
    {
        var (factory, world) = await BootAsync();
        await using var host = factory;
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await IdentityResellerWorld.StartViewingResellerAsync(factory, admin, world.Nova.ResellerId);
        Assert.Equal(HttpStatusCode.OK, (await viewing.GetAsync(StateEndpoint)).StatusCode);

        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync(StateEndpoint)).StatusCode);
    }

    /// <summary>The names on a page of accounts, in the order the page held them.</summary>
    /// <param name="page">The page's JSON.</param>
    /// <returns>The account names.</returns>
    private static string[] NamesOf(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => { return row.GetProperty("name").GetString()!; })
            .ToArray();
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
        var factory = await IdentityResellerWorld.BootAsync(
            _pg,
            configure: services =>
            {
                services.AddSingleton<IClock>(_clock);
            });

        return (factory, await IdentityResellerWorld.SeedViewAsWorldAsync(factory));
    }
}
