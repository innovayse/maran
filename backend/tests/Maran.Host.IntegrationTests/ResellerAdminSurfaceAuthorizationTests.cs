using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a reseller may not reach, over real HTTP against a real PostgreSQL (issue #179): the
/// administrator's endpoints answer a reseller 403 — a refusal by ROLE, which says nothing about any
/// tenant — and the one irreversible choice of an account deletion, skipping the final backup, is not
/// a reseller's to make.
/// </summary>
/// <remarks>
/// The route list is the enumeration. Each row is asked three questions — closed to a reseller, closed to
/// a customer, closed to an anonymous caller — and then the inverse control the rules require: the same
/// route is OPEN to an administrator, because a policy mutated to refuse everyone would satisfy every
/// refusal above it.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerAdminSurfaceAuthorizationTests : IAsyncLifetime
{
    /// <summary>The placeholder a route carries where the seeded reseller's id goes.</summary>
    private const string IdPlaceholder = "{id}";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerAdminSurfaceAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>
    /// Every administrator-only route a reseller could try: the reseller management routes, and the
    /// settings and audit routes that hold the whole server's configuration and history.
    /// </summary>
    /// <returns>Verb and route pairs; <c>{id}</c> stands for the seeded reseller.</returns>
    public static TheoryData<string, string> AdministratorRoutes()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/resellers" },
            { "GET", "/api/v1/resellers/choices" },
            { "GET", "/api/v1/resellers/{id}" },
            { "POST", "/api/v1/resellers" },
            { "PUT", "/api/v1/resellers/{id}/envelope" },
            { "POST", "/api/v1/resellers/{id}/suspend" },
            { "POST", "/api/v1/resellers/{id}/reactivate" },
            { "DELETE", "/api/v1/resellers/{id}" },
            { "GET", "/api/v1/security-policy" },
            { "PUT", "/api/v1/security-policy" },
            { "GET", "/api/v1/notifications/smtp" },
            { "GET", "/api/v1/audit" },
        };
    }

    /// <summary>
    /// A signed-in reseller is refused every administrator-only route with 403, whatever it carries; and
    /// nothing was changed by the attempt.
    /// </summary>
    /// <param name="method">The HTTP verb.</param>
    /// <param name="route">The route, with <c>{id}</c> for the reseller.</param>
    [Theory]
    [MemberData(nameof(AdministratorRoutes))]
    public async Task A_signed_in_reseller_is_refused_every_administrator_route_with_forbidden(
        string method,
        string route)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        // The reseller's OWN id, not a stranger's: authorization here is by role, and a check that only
        // refused another tenant's identifier would pass while leaving the surface open to its owner.
        var response = await SendAsync(alpha, method, route.Replace(IdPlaceholder, world.Alpha.ResellerId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, await world.AccountsAsync(async accounts => { return await accounts.Resellers.IgnoreQueryFilters().CountAsync(); }));
        Assert.Empty(await world.AuditEntriesAsync(
            AccountAuditActions.ResellerCreated,
            AccountAuditActions.ResellerEnvelopeChanged,
            AccountAuditActions.ResellerSuspended,
            AccountAuditActions.ResellerReactivated,
            AccountAuditActions.ResellerDeleted));
    }

    /// <summary>
    /// The inverse control: the same routes are not closed to an administrator, so the refusal above is
    /// the role and not a route that refuses everyone. Customers and anonymous callers stay refused.
    /// </summary>
    /// <param name="method">The HTTP verb.</param>
    /// <param name="route">The route, with <c>{id}</c> for the reseller.</param>
    [Theory]
    [MemberData(nameof(AdministratorRoutes))]
    public async Task The_same_routes_are_open_to_an_administrator_and_closed_to_a_customer_and_an_anonymous_caller(
        string method,
        string route)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var admin = await world.SignInAsync("admin");
        using var olive = await world.SignInAsync("olive");
        using var anonymous = world.Factory.CreateClient();
        var path = route.Replace(IdPlaceholder, world.Alpha.ResellerId.ToString(), StringComparison.Ordinal);

        var asAdministrator = await SendAsync(admin, method, path);
        var asCustomer = await SendAsync(olive, method, path);
        var asAnonymous = await SendAsync(anonymous, method, path);

        Assert.NotEqual(HttpStatusCode.Forbidden, asAdministrator.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, asAdministrator.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    /// <summary>
    /// A reseller that asks to delete an account WITHOUT its final backup is refused with 403
    /// <c>AccountFinalBackupSkipForbidden</c>, for an account it owns and for one it does not, so the
    /// refusal discloses nothing; the account survives and the refusal is journalled with the reseller
    /// as actor.
    /// </summary>
    [Fact]
    public async Task A_reseller_cannot_skip_the_final_backup_when_it_deletes_an_account()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var own = await alpha.DeleteAsync($"/api/v1/accounts/{world.Alpha.AccountId}?skipFinalBackup=true");
        var foreign = await alpha.DeleteAsync($"/api/v1/accounts/{world.Bravo.AccountId}?skipFinalBackup=true");

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal("AccountFinalBackupSkipForbidden", await ProblemDocument.CodeAsync(own));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("AccountFinalBackupSkipForbidden", await ProblemDocument.CodeAsync(foreign));
        Assert.Equal(3, await world.AccountsAsync(async accounts => { return await accounts.Accounts.IgnoreQueryFilters().CountAsync(); }));
        Assert.Empty(world.Agent.Deleted);
        var entries = await world.AuditEntriesAsync(AccountAuditActions.AccountDeleted);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal("alpha", entry.ActorUsername);
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>Issues one request, giving the POST and PUT routes an empty JSON object as a body.</summary>
    /// <param name="client">The caller's client.</param>
    /// <param name="method">The HTTP verb.</param>
    /// <param name="path">The route.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        return await client.SendAsync(request);
    }
}
