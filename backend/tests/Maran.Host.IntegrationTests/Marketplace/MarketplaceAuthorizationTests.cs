using System.Net;
using System.Reflection;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Marketplace.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// The marketplace's HTTP surface is administrator only, over real HTTP against a real PostgreSQL: on every
/// route a customer is refused 403 and nothing is written, an anonymous caller is refused 401, and the same
/// call as an administrator reaches the module and is answered by it - never by the authorization layer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdministratorSurfaceDiscoveredAuthorizationTests"/> already proves the 403 for every
/// administrator-only route it discovers in the route table, and does so with an empty body. This class adds
/// what that one cannot: the marketplace's routes written out by hand with the body each one needs, so the
/// refusal is observed on the call a real client sends; the inverse control on every one of them; the proof
/// that a refused call changes nothing; and the module catalogue's audience.
/// </para>
/// <para>
/// UNOBSERVED HERE: a reseller and an API key. Both are refused by the same administrator-only policy, which
/// the discovered-surface class exercises for resellers; an API key never reaches a policy that names no key
/// scheme.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MarketplaceAuthorizationTests : IAsyncLifetime
{
    /// <summary>The audit actions the marketplace writes; a refused call must leave none of them behind.</summary>
    private static readonly string[] MarketplaceActions =
    [
        MarketplaceClientAuditActions.MarketplaceEnabled,
        MarketplaceClientAuditActions.MarketplaceDisabled,
        MarketplaceClientAuditActions.ModuleInstallRequested,
        MarketplaceClientAuditActions.ModuleUpdateRequested,
        MarketplaceClientAuditActions.ModuleRemoveRequested,
        MarketplaceClientAuditActions.ModuleRollbackRequested,
    ];

    /// <summary>The task kinds the marketplace opens; a refused call must leave none of them behind.</summary>
    private static readonly string[] MarketplaceTaskKinds =
    [
        TaskKinds.ModuleInstall,
        TaskKinds.ModuleUpdate,
        TaskKinds.ModuleRemove,
        TaskKinds.ModuleRollback,
    ];

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MarketplaceAuthorizationTests(PostgresFixture postgres)
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
    /// Every route the controller declares has a row in the table these tests walk, and every row is a
    /// route the controller declares, so a new route cannot ship without its authorization tests.
    /// </summary>
    [Fact]
    public void Every_marketplace_route_the_controller_declares_is_in_the_authorization_table()
    {
        const BindingFlags DeclaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var declared = typeof(MarketplaceController)
            .GetMethods(DeclaredOnly)
            .SelectMany(method => { return method.GetCustomAttributes<HttpMethodAttribute>(); })
            .Select(attribute => { return attribute.HttpMethods.First() + " " + attribute.Template; })
            .Order(StringComparer.Ordinal)
            .ToList();
        var tabled = MarketplaceRoutes.All
            .Select(route => { return route.Key; })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(11, declared.Count);
        Assert.Equal(declared, tabled);
    }

    /// <summary>
    /// A signed-in customer is refused 403 on every marketplace route, with a well-formed body and the
    /// right password, and the refused calls reach neither the marketplace nor the database.
    /// </summary>
    [Fact]
    public async Task A_customer_is_refused_every_marketplace_route_with_forbidden_and_nothing_is_written()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);
        var requestsBefore = world.Marketplace.RequestCount;
        var auditBefore = await AuditCountAsync(world, administrator);
        using var customer = await world.SignInAsync(MarketplaceWorld.CustomerName);

        var offenders = new List<string>();
        foreach (var route in MarketplaceRoutes.All)
        {
            var answer = await customer.SendAsync(route.Method, route.FullPath, route.Body);
            if (answer.Status != HttpStatusCode.Forbidden)
            {
                offenders.Add($"{route.Key} -> {(int)answer.Status}");
            }
        }

        Assert.True(offenders.Count == 0, "Answered something other than 403: " + string.Join("; ", offenders));
        Assert.Equal(requestsBefore, world.Marketplace.RequestCount);
        Assert.Equal(auditBefore, await AuditCountAsync(world, administrator));
        Assert.Empty(await world.LedgerAsync());
        foreach (var kind in MarketplaceTaskKinds)
        {
            Assert.Equal(0, await world.TaskCountAsync(kind));
        }
    }

    /// <summary>
    /// The inverse control: the same calls as an administrator are not refused by authorization on any
    /// route - each is answered by the marketplace itself with its own status and code - and the writes
    /// the customer's calls would have made are visible, so the "nothing is written" probe can see them.
    /// </summary>
    [Fact]
    public async Task An_administrator_gets_the_modules_own_answer_on_every_marketplace_route()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");

        var offenders = new List<string>();
        foreach (var route in MarketplaceRoutes.All)
        {
            var answer = await administrator.SendAsync(route.Method, route.FullPath, route.Body);
            var code = route.AdministratorCode is null ? null : answer.Code();

            if (answer.Status != route.AdministratorStatus || code != route.AdministratorCode)
            {
                offenders.Add($"{route.Key} -> {(int)answer.Status} {code}");
            }
        }

        Assert.True(offenders.Count == 0, "Answered otherwise than the module does: " + string.Join("; ", offenders));
        Assert.True(await world.SettingsRowExistsAsync());
        Assert.Single(await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.MarketplaceDisabled));
        Assert.Equal(0, world.Marketplace.RequestCount);
    }

    /// <summary>An anonymous caller is refused 401 on every marketplace route.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_every_marketplace_route_with_unauthorized()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var anonymous = new PanelCaller(world.Factory.CreateClient());

        var offenders = new List<string>();
        foreach (var route in MarketplaceRoutes.All)
        {
            var answer = await anonymous.SendAsync(route.Method, route.FullPath, route.Body);
            if (answer.Status != HttpStatusCode.Unauthorized)
            {
                offenders.Add($"{route.Key} -> {(int)answer.Status}");
            }
        }

        Assert.True(offenders.Count == 0, "Answered something other than 401: " + string.Join("; ", offenders));
        Assert.Equal(0, world.Marketplace.RequestCount);
    }

    /// <summary>
    /// The module catalogue lists the marketplace for an administrator and leaves it out for a customer,
    /// both over a real sign-in, so the screen is not offered to someone the routes would refuse.
    /// </summary>
    [Fact]
    public async Task The_module_catalogue_offers_the_marketplace_to_an_administrator_and_not_to_a_customer()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        using var customer = await world.SignInAsync(MarketplaceWorld.CustomerName);

        var forAdministrator = await ModuleNamesAsync(administrator);
        var forCustomer = await ModuleNamesAsync(customer);

        Assert.Contains("marketplace", forAdministrator);
        Assert.DoesNotContain("marketplace", forCustomer);
        Assert.NotEmpty(forCustomer);
    }

    /// <summary>Reads the names of the modules <c>GET /api/v1/modules</c> offers the caller.</summary>
    /// <param name="caller">The signed-in caller.</param>
    /// <returns>The module names.</returns>
    private static async Task<List<string>> ModuleNamesAsync(PanelCaller caller)
    {
        var answer = await caller.GetAsync("/api/v1/modules");
        Assert.Equal(HttpStatusCode.OK, answer.Status);

        return answer.Json()
            .EnumerateArray()
            .Select(module => { return module.GetProperty("name").GetString()!; })
            .ToList();
    }

    /// <summary>Counts the marketplace's own audit entries, whoever wrote them.</summary>
    /// <param name="world">The booted world.</param>
    /// <param name="administrator">The signed-in administrator.</param>
    /// <returns>The number of entries across every marketplace action.</returns>
    private static async Task<int> AuditCountAsync(MarketplaceWorld world, PanelCaller administrator)
    {
        var total = 0;
        foreach (var action in MarketplaceActions)
        {
            total += (await MarketplaceWorld.AuditAsync(administrator, action)).Count;
        }

        return total;
    }
}
