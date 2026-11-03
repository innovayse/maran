using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Team-member grants cover Sites, SSL certificates, background tasks and PHP over real HTTP, and a
/// removed member loses all of them on the request after the removal, with the token they already hold
/// (issue #340).
/// </summary>
/// <remarks>
/// <para>
/// Every refusal here has its inverse control in the same class: the member who WAS given the feature,
/// and the owner, reach the same route. The plan is the default one (every feature) except where a test
/// states otherwise, so a refusal is the PERSON's and not the plan's.
/// </para>
/// <para>
/// UNOBSERVED HERE: the agent. The routes asked are the ones whose handlers read only the database, so
/// a 200 proves the gate let the call through and says nothing about what the host would then do.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SubUserGatedModulesEndpointTests : IAsyncLifetime
{
    /// <summary>The code a member refused by the owner's choice is answered with.</summary>
    private const string NotGranted = "AccountFeatureNotGranted";

    /// <summary>The code an account whose plan does not sell the feature is answered with.</summary>
    private const string PlanLacksFeature = "PlanFeatureNotIncluded";

    /// <summary>The domain of the site the first account owns.</summary>
    private const string OwnSiteDomain = "own.example.com";

    /// <summary>The domain of the site the second account owns.</summary>
    private const string OtherSiteDomain = "other.example.com";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The first account's site, set when the host boots.</summary>
    private Guid _ownSite;

    /// <summary>The second account's site, set when the host boots.</summary>
    private Guid _otherSite;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SubUserGatedModulesEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>One row per gated module: the feature that opens it and a database-only route that reads it.</summary>
    public static TheoryData<AccountFeature, string> GatedRoutes()
    {
        return new TheoryData<AccountFeature, string>
        {
            { AccountFeature.Sites, "/api/v1/sites" },
            { AccountFeature.Ssl, "/api/v1/certificates" },
            { AccountFeature.Tasks, "/api/v1/tasks" },
        };
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

    /// <summary>A member who was given no feature is refused every gated module, and told the owner chose it.</summary>
    [Theory]
    [MemberData(nameof(GatedRoutes))]
    public async Task A_member_with_no_grants_is_refused_the_module(AccountFeature feature, string route)
    {
        _ = feature;
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.None);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");

        var refused = await member.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await AssertCodeAsync(refused, NotGranted);
    }

    /// <summary>The inverse control: granted exactly the module's feature, the member reaches it, and so does the owner.</summary>
    [Theory]
    [MemberData(nameof(GatedRoutes))]
    public async Task A_member_given_the_feature_and_the_owner_reach_the_module(AccountFeature feature, string route)
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", feature);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(route)).StatusCode);
    }

    /// <summary>A grant covers its own module and no other: Sites does not open certificates or tasks.</summary>
    [Fact]
    public async Task A_grant_opens_its_own_module_and_not_the_others()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.Sites);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/certificates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/tasks")).StatusCode);
    }

    /// <summary>Changing a site's PHP version needs the Php grant on top of Sites.</summary>
    [Fact]
    public async Task Switching_the_php_version_needs_the_php_grant_as_well_as_sites()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "sites-only", AccountFeature.Sites);
        await SeedMemberAsync(factory, "sites-and-php", AccountFeature.Sites | AccountFeature.Php);
        using var withoutPhp = await EndpointHarness.SignInAsync(factory, "sites-only");
        using var withPhp = await EndpointHarness.SignInAsync(factory, "sites-and-php");
        var route = $"/api/v1/sites/{_ownSite}/php-version";

        var refused = await withoutPhp.PostAsJsonAsync(route, new { PhpVersion = "8.3" });
        var reached = await withPhp.PostAsJsonAsync(route, new { PhpVersion = "8.3" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await AssertCodeAsync(refused, NotGranted);

        // The inverse control: past the gate the handler gives its own answer, whatever it is.
        Assert.NotEqual(HttpStatusCode.Forbidden, reached.StatusCode);
    }

    /// <summary>The Php grant alone is not enough: the site endpoints still need Sites.</summary>
    [Fact]
    public async Task The_php_grant_alone_does_not_open_the_site_endpoints()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "php-only", AccountFeature.Php);
        using var member = await EndpointHarness.SignInAsync(factory, "php-only");

        var refused = await member.PostAsJsonAsync(
            $"/api/v1/sites/{_ownSite}/php-version", new { PhpVersion = "8.3" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await AssertCodeAsync(refused, NotGranted);
    }

    /// <summary>Removing a member ends their access to every gated module on the very next request, with the token they hold.</summary>
    [Theory]
    [MemberData(nameof(GatedRoutes))]
    public async Task A_removed_members_token_is_refused_at_once(AccountFeature feature, string route)
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "deploy", AccountFeature.All);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        _ = feature;
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(route)).StatusCode);

        var removed = await owner.DeleteAsync($"/api/v1/me/sub-users/{memberId}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(route)).StatusCode);
    }

    /// <summary>
    /// The membership is asked on every customer request, not only on the gated modules: a removed
    /// member's token is refused on an endpoint that carries no feature at all.
    /// </summary>
    [Fact]
    public async Task A_removed_members_token_is_refused_on_an_endpoint_that_has_no_feature()
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "deploy", AccountFeature.None);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/me/accounts")).StatusCode);

        await DeleteMembershipAsync(factory, memberId);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/me/accounts")).StatusCode);
    }

    /// <summary>The inverse control for the per-request check: the owner's token, untouched, keeps working.</summary>
    [Fact]
    public async Task Removing_one_member_leaves_the_owner_and_other_members_working()
    {
        await using var factory = await BootAsync();
        var removedId = await SeedMemberAsync(factory, "gone", AccountFeature.Sites);
        await SeedMemberAsync(factory, "stays", AccountFeature.Sites);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var stays = await EndpointHarness.SignInAsync(factory, "stays");

        await DeleteMembershipAsync(factory, removedId);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stays.GetAsync("/api/v1/sites")).StatusCode);
    }

    /// <summary>A member with the Sites grant still gets 404, not 403, for another account's site.</summary>
    [Fact]
    public async Task A_granted_member_gets_404_for_another_accounts_site()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.Sites);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/sites/{_ownSite}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/sites/{_otherSite}")).StatusCode);
    }

    /// <summary>The plan is asked as well: an owner on a plan that does not sell Sites is refused with the plan's code.</summary>
    [Fact]
    public async Task An_account_whose_plan_does_not_sell_sites_is_refused_with_the_plan_code()
    {
        await using var factory = await BootAsync();
        var plan = Plan.Define(
            Guid.NewGuid(), "No sites", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.All & ~AccountFeature.Sites);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "gamma", plan);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-c", accountId);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-c");

        var refused = await owner.GetAsync("/api/v1/sites");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await AssertCodeAsync(refused, PlanLacksFeature);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/tasks")).StatusCode);
    }

    /// <summary>Every feature the panel can gate is offered to the owner for granting, the four new ones included.</summary>
    [Fact]
    public async Task The_grantable_features_include_sites_ssl_tasks_and_php()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        using var body = JsonDocument.Parse(
            await (await owner.GetAsync("/api/v1/me/sub-users/features")).Content.ReadAsStringAsync());

        var offered = body.RootElement.EnumerateArray()
            .Select(row => { return row.GetProperty("feature").GetString(); })
            .ToList();
        Assert.Contains("sites", offered);
        Assert.Contains("ssl", offered);
        Assert.Contains("tasks", offered);
        Assert.Contains("php", offered);
    }

    /// <summary>Boots the panel with two accounts (an owner and a site each) and an administrator.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountA) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, accountB) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", accountA);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", accountB);
        _ownSite = await SeedSiteAsync(factory, accountA, OwnSiteDomain);
        _otherSite = await SeedSiteAsync(factory, accountB, OtherSiteDomain);

        return factory;
    }

    /// <summary>Stores a PHP site of an account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account that owns the site.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <returns>The site's id.</returns>
    private static async Task<Guid> SeedSiteAsync(WebApplicationFactory<Program> factory, Guid accountId, string domain)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var id = Guid.NewGuid();
        sites.Sites.Add(new Site(
            id, accountId, domain, [], SiteBackendType.Php, "8.3", string.Empty, $"/home/x/sites/{domain}", now));
        await sites.SaveChangesAsync();

        return id;
    }

    /// <summary>Stores an active team member of the first account with the given grants.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The member's login name.</param>
    /// <param name="grants">The features the owner gave them.</param>
    /// <returns>The member's login id.</returns>
    private static async Task<Guid> SeedMemberAsync(
        WebApplicationFactory<Program> factory,
        string username,
        AccountFeature grants)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var ownerMembership = await (
            from membership in identity.AccountMemberships
            join user in identity.Users on membership.UserId equals user.Id
            where user.Username == "owner-a"
            select membership).SingleAsync();

        var member = User.Invite(Guid.NewGuid(), username, $"{username}@example.com", now);
        member.Activate(hasher.Hash(EndpointHarness.Password));
        identity.Users.Add(member);
        identity.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, ownerMembership.AccountId, grants, now));
        await identity.SaveChangesAsync();

        return member.Id;
    }

    /// <summary>
    /// Deletes a login's memberships and nothing else, leaving the access token the login already holds
    /// untouched: the state a removed member's token is in until it expires.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="userId">The login whose memberships go.</param>
    private static async Task DeleteMembershipAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var rows = await identity.AccountMemberships.Where(row => row.UserId == userId).ToListAsync();
        identity.AccountMemberships.RemoveRange(rows);
        await identity.SaveChangesAsync();
    }

    /// <summary>Asserts the refusal carries the machine code the SPA branches on.</summary>
    /// <param name="response">The refused response.</param>
    /// <param name="code">The expected code.</param>
    private static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
