using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>/api/v1/view-as</c> over real HTTP and a real journal (issues #317, #314): an administrator
/// views the panel as an account's login through a scoped customer token that names them as actor,
/// every start and end is journalled with both identities, and every way the token could be stretched
/// or escalated is refused. Threat note: docs/superpowers/notes/2026-11-02-view-as-impersonation-threat-note.md.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ViewAsEndpointTests : IAsyncLifetime
{
    private const string Endpoint = "/api/v1/view-as";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The panel clock, moved past the view-as lifetime by the expiry test.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ViewAsEndpointTests(PostgresFixture postgres)
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
    /// Starting answers a customer token for the account's login, carrying the administrator as actor,
    /// and that token reads exactly the viewed account's data.
    /// </summary>
    [Fact]
    public async Task Starting_issues_a_scoped_customer_token_carrying_the_administrator_as_actor()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var acme = await AccountIdAsync(factory, "acme");

        var response = await admin.PostAsJsonAsync(Endpoint, new { AccountId = acme });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        Assert.Equal(nameof(UserRole.Customer), token.GetClaim(PanelClaimTypes.Role).Value);
        Assert.Equal(acme.ToString(), token.GetClaim(PanelClaimTypes.AccountId).Value);
        Assert.Equal("ViewAs", token.GetClaim(PanelClaimTypes.SessionKind).Value);
        using (var actor = JsonDocument.Parse(token.GetClaim(PanelClaimTypes.Actor).Value))
        {
            Assert.Equal("admin", actor.RootElement.GetProperty(PanelClaimTypes.Username).GetString());
        }

        Assert.Equal("acme", body.RootElement.GetProperty("viewing").GetProperty("accountName").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var viewing = ClientWith(factory, accessToken);
        using var me = JsonDocument.Parse(await viewing.GetStringAsync("/api/v1/accounts/me"));
        Assert.Equal("acme.example.com", me.RootElement.GetProperty("primaryDomain").GetString());

        using var state = JsonDocument.Parse(await viewing.GetStringAsync(Endpoint));
        Assert.Equal("admin", state.RootElement.GetProperty("viewing").GetProperty("operatorUsername").GetString());
    }

    /// <summary>The start and the end are journalled with the administrator, and the end with the impersonator column.</summary>
    [Fact]
    public async Task Start_and_end_are_journalled_with_the_administrator_and_the_viewed_identity()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");

        var ended = await viewing.DeleteAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var adminId = await context.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync();
        var started = await context.AuditEvents.SingleAsync(e => e.Action == AuditActions.ViewAsStarted);
        var stopped = await context.AuditEvents.SingleAsync(e => e.Action == AuditActions.ViewAsEnded);

        Assert.Equal(adminId, started.ActorUserId);
        Assert.Contains("olive", started.Subject, StringComparison.Ordinal);
        Assert.True(started.Succeeded);
        Assert.Null(started.ImpersonatorUserId);

        Assert.Equal(adminId, stopped.ActorUserId);
        Assert.Contains("acme", stopped.Subject, StringComparison.Ordinal);
        Assert.Equal(adminId, stopped.ImpersonatorUserId);
        Assert.Equal("admin", stopped.ImpersonatorUsername);
    }

    /// <summary>Something the operator does while viewing is journalled under the customer with the operator beside it.</summary>
    [Fact]
    public async Task An_action_taken_while_viewing_records_the_operator_as_impersonator()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");

        var pinned = await viewing.PostAsJsonAsync("/api/v1/me/pinned-pages", new { ItemKey = "sites-list" });

        Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entry = await context.AuditEvents.SingleAsync(e => e.Action == AuditActions.PagePinned);
        Assert.Equal("olive", entry.ActorUsername);
        Assert.Equal("admin", entry.ImpersonatorUsername);
    }

    /// <summary>A customer may not view as anybody, and nothing is issued.</summary>
    [Fact]
    public async Task A_customer_is_refused()
    {
        await using var factory = await BootAsync();
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        var globex = await AccountIdAsync(factory, "globex");

        var response = await customer.PostAsJsonAsync(Endpoint, new { AccountId = globex });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await ViewAsSessionCountAsync(factory));
    }

    /// <summary>An account that does not exist answers 404, journals the refusal, and issues nothing.</summary>
    [Fact]
    public async Task A_nonexistent_account_answers_404()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PostAsJsonAsync(Endpoint, new { AccountId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await ViewAsSessionCountAsync(factory));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False((await context.AuditEvents.SingleAsync(e => e.Action == AuditActions.ViewAsStarted)).Succeeded);
    }

    /// <summary>A view-as token can start no second view-as session and change no credential or session.</summary>
    [Fact]
    public async Task A_view_as_token_cannot_escalate()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");
        var globex = await AccountIdAsync(factory, "globex");

        var again = await viewing.PostAsJsonAsync(Endpoint, new { AccountId = globex });
        var password = await viewing.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { CurrentPassword = EndpointHarness.Password, NewPassword = "another correct horse battery" });
        var sessions = await viewing.GetAsync("/api/v1/sessions");
        var everywhere = await viewing.PostAsync("/api/v1/auth/logout-all", null);

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sessions.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, everywhere.StatusCode);
        Assert.Equal(1, await ViewAsSessionCountAsync(factory));

        // The customer's own password still works: nothing above reached the handler.
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync(Endpoint)).StatusCode);
    }

    /// <summary>After Back the view-as token is refused at once, and the administrator's own token is untouched.</summary>
    [Fact]
    public async Task After_back_the_view_as_token_is_refused_and_the_administrator_is_untouched()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");

        Assert.Equal(HttpStatusCode.OK, (await viewing.DeleteAsync(Endpoint)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync(Endpoint)).StatusCode);
        using var state = JsonDocument.Parse(await admin.GetStringAsync(Endpoint));
        Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("viewing").ValueKind);
    }

    /// <summary>The view-as token stops working when the session's lifetime passes, without waiting for the token.</summary>
    [Fact]
    public async Task The_view_as_token_stops_working_when_the_session_expires()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");
        Assert.Equal(HttpStatusCode.OK, (await viewing.GetAsync(Endpoint)).StatusCode);

        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync(Endpoint)).StatusCode);
    }

    /// <summary>Signing the administrator out everywhere ends the view-as session too.</summary>
    [Fact]
    public async Task Signing_the_administrator_out_everywhere_ends_viewing()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartAsync(factory, admin, "acme");

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewing.GetAsync(Endpoint)).StatusCode);
    }

    /// <summary>Starts viewing as an account and returns a client carrying the view-as token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="accountName">The account to view as.</param>
    /// <returns>A client the caller disposes.</returns>
    private static async Task<HttpClient> StartAsync(WebApplicationFactory<Program> factory, HttpClient admin, string accountName)
    {
        var response = await admin.PostAsJsonAsync(Endpoint, new { AccountId = await AccountIdAsync(factory, accountName) });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ClientWith(factory, body.RootElement.GetProperty("accessToken").GetString()!);
    }

    /// <summary>Creates a client carrying a bearer token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accessToken">The token.</param>
    /// <returns>A client the caller disposes.</returns>
    private static HttpClient ClientWith(WebApplicationFactory<Program> factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Reads an account's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    /// <returns>Its id.</returns>
    private static async Task<Guid> AccountIdAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Maran.Modules.Accounts.Persistence.AccountsDbContext>();
        return await context.Accounts.Where(a => a.Name == accountName).Select(a => a.Id).SingleAsync();
    }

    /// <summary>Counts the view-as sessions ever issued.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The count.</returns>
    private static async Task<int> ViewAsSessionCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await context.Sessions.CountAsync(s => s.Kind == SessionKind.ViewAs);
    }

    /// <summary>Boots the host with an administrator and a customer, olive, who owns acme; globex has its own owner.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IClock>(_clock);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, globex) = await EndpointHarness.SeedAccountAsync(factory, "globex");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", acme);
        await EndpointHarness.SeedCustomerAsync(factory, "gus", globex);

        return factory;
    }
}
