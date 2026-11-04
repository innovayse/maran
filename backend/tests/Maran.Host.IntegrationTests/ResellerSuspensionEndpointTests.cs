using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Suspending a reseller over real HTTP (issue #179, decision D2): the reseller's LOGIN stops working at
/// once, its sessions are revoked and its live token starts no view-as, and its customers' accounts and
/// logins are untouched, as is every other reseller.
/// </summary>
/// <remarks>
/// The administrator's own endpoint is used, so the real event reaches Identity's subscriber. What a
/// suspended reseller's live token may WRITE in the accounts module is asserted by that module's tests;
/// the identity-side obligation is that the login, the sessions and the view-as door are closed.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerSuspensionEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerSuspensionEndpointTests(PostgresFixture postgres)
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

    /// <summary>A suspended reseller is refused at sign-in exactly as a wrong password is.</summary>
    [Fact]
    public async Task A_suspended_reseller_cannot_sign_in()
    {
        await using var factory = await BootAsync();
        var nova = await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/suspend", null)).StatusCode);
        using var anonymous = factory.CreateClient();

        var refused = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });
        var wrongPassword = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = "wrong-password-entirely" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, refused.StatusCode);
        Assert.Equal("InvalidCredentialsUnauthorized", await CodeOfAsync(refused));
    }

    /// <summary>
    /// Suspending revokes every session of the reseller's login that was still live, so its refresh cookie
    /// stops working and no session of the login is left usable; a session already rotated away keeps the
    /// reason it was ended with.
    /// </summary>
    [Fact]
    public async Task Suspending_a_reseller_revokes_its_sessions_and_refuses_its_refresh()
    {
        await using var factory = await BootAsync();
        var nova = await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        using var reseller = await IdentityResellerWorld.SignInWithCookieAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        // The control: before the suspension the cookie refreshes, so the refusal below is the suspension's.
        Assert.Equal(HttpStatusCode.OK, (await reseller.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        using var again = await IdentityResellerWorld.SignInWithCookieAsync(factory, "nova");
        await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/suspend", null);

        var refreshed = await again.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sessions = await identity.Sessions.AsNoTracking().Where(s => s.UserId == nova.UserId).ToListAsync();

        // Three sessions exist: the first one's refresh ROTATED it before the suspension (a session that
        // is revoked keeps the first reason it was given, so it reads Rotated, which is already unusable),
        // and the two that were live when the suspension came are revoked by it. None is left usable.
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, session =>
        {
            Assert.NotNull(session.Revocation);
        });
        Assert.Equal(1, sessions.Count(session => { return session.Revocation?.Reason == SessionRevocationReason.Rotated; }));
        Assert.Equal(
            2,
            sessions.Count(session => { return session.Revocation?.Reason == SessionRevocationReason.ResellerSuspended; }));
    }

    /// <summary>
    /// The reseller's customers are not suspended with it: their account stays active, their login signs
    /// in and reads the account, and another reseller's login is not touched either.
    /// </summary>
    [Fact]
    public async Task Suspending_a_reseller_leaves_its_customers_and_every_other_reseller_untouched()
    {
        await using var factory = await BootAsync();
        var nova = await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        var orbit = await IdentityResellerWorld.SeedResellerAsync(factory, "orbit");
        var acme = await IdentityResellerWorld.SeedResellerAccountAsync(factory, nova.ResellerId, "acme", "olive");
        await IdentityResellerWorld.SeedResellerAccountAsync(factory, orbit.ResellerId, "globex", "gus");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var suspended = await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/suspend", null);

        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        using var olive = await EndpointHarness.SignInAsync(factory, "olive");
        using var account = JsonDocument.Parse(await olive.GetStringAsync("/api/v1/accounts/me"));
        Assert.Equal("acme.example.com", account.RootElement.GetProperty("primaryDomain").GetString());
        using var gus = await EndpointHarness.SignInAsync(factory, "gus");
        Assert.Equal(HttpStatusCode.OK, (await gus.GetAsync("/api/v1/accounts/me")).StatusCode);
        using var other = await EndpointHarness.SignInAsync(factory, "orbit");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/reseller/me")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Assert.Equal(AccountStatus.Active, await accounts.Accounts.IgnoreQueryFilters().Where(a => a.Id == acme).Select(a => a.Status).SingleAsync());
    }

    /// <summary>
    /// An access token the reseller held before the suspension is still a valid bearer for up to its
    /// lifetime, and it cannot start a view-as: the start re-reads the operator's standing.
    /// </summary>
    [Fact]
    public async Task A_suspended_resellers_live_token_cannot_start_a_view_as()
    {
        await using var factory = await BootAsync();
        var nova = await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        var acme = await IdentityResellerWorld.SeedResellerAccountAsync(factory, nova.ResellerId, "acme", "olive");
        using var reseller = await EndpointHarness.SignInAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        // The control: the same token starts one while the reseller is active.
        using (var control = await IdentityResellerWorld.StartViewingAccountAsync(factory, reseller, acme))
        {
            Assert.Equal(HttpStatusCode.OK, (await control.GetAsync("/api/v1/view-as")).StatusCode);
        }

        await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/suspend", null);
        var response = await reseller.PostAsJsonAsync("/api/v1/view-as", new { AccountId = acme });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ViewAsAccountNotFound", await CodeOfAsync(response));
        Assert.Equal(1, await IdentityResellerWorld.ViewAsSessionCountAsync(factory));
        var started = await IdentityResellerWorld.AuditEntriesAsync(factory, IdentityAuditActions.ViewAsStarted);
        Assert.Equal([true, false], started.Select(entry => { return entry.Succeeded; }).ToArray());
        Assert.All(started, entry => { Assert.Equal(nova.UserId, entry.ActorUserId); });
    }

    /// <summary>Reactivating a reseller gives its login back.</summary>
    /// <remarks>The inverse control of the suspension tests: a gate that refuses everything passes the others.</remarks>
    [Fact]
    public async Task Reactivating_a_reseller_restores_its_sign_in()
    {
        await using var factory = await BootAsync();
        var nova = await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/suspend", null);
        using var anonymous = factory.CreateClient();
        var refused = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });

        var reactivated = await admin.PostAsync($"/api/v1/resellers/{nova.ResellerId}/reactivate", null);
        var signedIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "nova", Password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>Boots the host with an administrator and a policy that does not force two-factor.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private Task<WebApplicationFactory<Program>> BootAsync()
    {
        return IdentityResellerWorld.BootAsync(_pg);
    }

    /// <summary>Reads the machine code out of a problem answer.</summary>
    /// <param name="response">The refused answer.</param>
    /// <returns>Its <c>code</c>.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}
