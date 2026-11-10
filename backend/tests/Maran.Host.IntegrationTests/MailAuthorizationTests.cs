using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Mail.Controllers;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The mail surface over real HTTP against real PostgreSQL: the IDOR fixture every tenant-scoped endpoint
/// is required to have (rules/testing.md, Definition of Done 3), and the role gate the server and queue
/// endpoints carry instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two shapes of authorisation in one fixture, because the contrast is the point.</b> The domain,
/// mailbox, alias and autoresponder controllers are open to any signed-in caller and scoped by the tenant
/// query filter on <see cref="MailDbContext"/>, so another customer's resource must answer <b>404</b>: a 403
/// would say "this mailbox exists but is not yours", the fact an attacker wanted — and a password change or
/// a DKIM rotation aimed at a stranger's row would be an account takeover or a mail outage, not a disclosure.
/// The server and queue controllers have no tenant dimension (there is one mail stack on this machine), so
/// they are gated by ROLE and answer <b>403</b>.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control</b>, because a gate mutated to refuse everything
/// passes every test that only hands it something it must reject: a customer reads and deletes their OWN
/// resources, and the administrator reads the server status. The seed runs the module's real migration, so
/// the unique indexes the unit tests' in-memory store cannot enforce are exercised here.
/// </para>
/// <para>The agent is the only substitution, and only because it cannot be present.</para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MailAuthorizationTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The hash stored on the stranger's mailbox, which no request in these tests may change.</summary>
    private const string StrangerHash =
        "$argon2id$v=19$m=65536,t=3,p=2$c3RyYW5nZXJzYWx0MTIzNA$c3RyYW5nZXJoYXNoc3RyYW5nZXJoYXNoc3RyYW5nZXJoYXNo";

    /// <summary>A throwaway 256-bit key the host is booted with for encryption and token signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every customer route that names one resource, with a body its binder accepts for PUT and POST.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_resource_scoped_customer_route_is_covered_by_the_idor_fixture"/> rather than trusted.
    /// </remarks>
    public static TheoryData<string, string, string> ResourceScopedEndpoints()
    {
        return new TheoryData<string, string, string>
        {
            { "DELETE", "/api/v1/mail-domains/{domain}", string.Empty },
            { "POST", "/api/v1/mail-domains/{domain}/dkim-rotation", string.Empty },
            { "GET", "/api/v1/mail-domains/{domain}/dns-records", string.Empty },
            { "PUT", "/api/v1/mailboxes/{mailbox}", "mailbox-update" },
            { "PUT", "/api/v1/mailboxes/{mailbox}/password", "mailbox-password" },
            { "DELETE", "/api/v1/mailboxes/{mailbox}", string.Empty },
            { "GET", "/api/v1/mailboxes/{mailbox}/autoresponder", string.Empty },
            { "PUT", "/api/v1/mailboxes/{mailbox}/autoresponder", "autoresponder" },
            { "DELETE", "/api/v1/mailboxes/{mailbox}/autoresponder", string.Empty },
            { "PUT", "/api/v1/mail-aliases/{alias}", "alias-update" },
            { "DELETE", "/api/v1/mail-aliases/{alias}", string.Empty },
        };
    }

    /// <summary>Every route of the three customer-facing list-and-create controllers, for the anonymous refusal.</summary>
    public static TheoryData<string, string> CustomerEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/mail/choices" },
            { "GET", "/api/v1/mail-domains" },
            { "POST", "/api/v1/mail-domains" },
            { "GET", "/api/v1/mailboxes" },
            { "POST", "/api/v1/mailboxes" },
            { "GET", "/api/v1/mail-aliases" },
            { "POST", "/api/v1/mail-aliases" },
        };
    }

    /// <summary>Every route of the administrator-only server and queue controllers.</summary>
    /// <remarks>Completeness is asserted by <see cref="Every_server_route_is_covered_by_the_role_fixture"/>.</remarks>
    public static TheoryData<string, string> AdministratorEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/mail-server" },
            { "POST", "/api/v1/mail-server/enable" },
            { "POST", "/api/v1/mail-server/disable" },
            { "PUT", "/api/v1/mail-server/settings" },
            { "GET", "/api/v1/mail-queue" },
            { "GET", "/api/v1/mail-queue/count" },
            { "DELETE", "/api/v1/mail-queue/{queue}" },
            { "POST", "/api/v1/mail-queue/flush" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MailAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every customer mail endpoint.</summary>
    [Theory]
    [MemberData(nameof(CustomerEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_customer_mail_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path, "create");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants mail resource is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(ResourceScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_mail_resource_is_answered_not_found_and_never_forbidden(
        string method,
        string path,
        string body)
    {
        // The SPECIFIC answer, not "some refusal": a distinct refusal confirms the neighbour's mailbox
        // exists, and a password change or a DKIM rotation that got past it would be a takeover or an outage.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.Stranger), body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown mail identifier answers not found, the same as another tenants row.</summary>
    [Theory]
    [MemberData(nameof(ResourceScopedEndpoints))]
    public async Task An_unknown_mail_identifier_answers_not_found_rather_than_failing(
        string method,
        string path,
        string body)
    {
        // The SAME answer as another tenant's row, or the difference between them IS the oracle.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, WorldIds.Unknown), body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer deleting another tenants mailbox leaves that mailbox in place.</summary>
    [Fact]
    public async Task A_customer_deleting_another_tenants_mailbox_leaves_that_mailbox_in_place()
    {
        // The status code is not the whole property: a 404 from a handler that had already deleted the row
        // would satisfy the theory above. The row is read back past the tenant filter and must still be there.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync($"/api/v1/mailboxes/{world.Stranger.MailboxId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(["info"], await MailboxLocalPartsAsync(factory, world.Stranger.MailboxId));
    }

    /// <summary>A customer deleting their own mailbox removes it.</summary>
    [Fact]
    public async Task A_customer_deleting_their_own_mailbox_removes_it()
    {
        // The inverse control: a delete route that answered NotFound to EVERYBODY would make the survival of
        // a stranger's row true for a reason that has nothing to do with tenancy.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync($"/api/v1/mailboxes/{world.Own.MailboxId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await MailboxLocalPartsAsync(factory, world.Own.MailboxId));
    }

    /// <summary>A customer changing another tenants mailbox password leaves its hash untouched.</summary>
    [Fact]
    public async Task A_customer_changing_another_tenants_mailbox_password_leaves_its_hash_untouched()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(
            client, "PUT", $"/api/v1/mailboxes/{world.Stranger.MailboxId}/password", "mailbox-password");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailDbContext>();
        var hashes = await mail.Mailboxes
            .IgnoreQueryFilters()
            .Where(row => row.Id == world.Stranger.MailboxId)
            .Select(row => row.PasswordHash)
            .ToListAsync();

        Assert.Equal([StrangerHash], hashes);
    }

    /// <summary>A customer rotating another tenants DKIM key leaves its key unchanged.</summary>
    [Fact]
    public async Task A_customer_rotating_another_tenants_dkim_key_leaves_its_key_unchanged()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsync($"/api/v1/mail-domains/{world.Stranger.DomainId}/dkim-rotation", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailDbContext>();
        var selectors = await mail.DkimKeys
            .IgnoreQueryFilters()
            .Where(row => row.MailDomainId == world.Stranger.DomainId)
            .Select(row => row.Selector)
            .ToListAsync();

        Assert.Equal(["m20261107-1"], selectors);
    }

    /// <summary>A customer reading their own mailbox list is shown it, and only it.</summary>
    [Fact]
    public async Task Listing_mailboxes_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/mailboxes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var addresses = body.RootElement.GetProperty("items").EnumerateArray().Select(row =>
        {
            return row.GetProperty("address").GetString();
        }).ToList();

        Assert.Equal(["info@own.example.com"], addresses);
        Assert.DoesNotContain("passwordHash", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A customer naming another tenants account in a filter is listed nothing.</summary>
    [Theory]
    [InlineData("/api/v1/mail-domains")]
    [InlineData("/api/v1/mailboxes")]
    [InlineData("/api/v1/mail-aliases")]
    public async Task A_customer_naming_another_tenants_account_is_listed_nothing(string route)
    {
        // The accountId filter is ANDed with the tenant filter, never in place of it. The own-rows listing
        // above is the inverse control: an endpoint that ignored the parameter would answer with the
        // customer's own row here, and one that let it replace the tenant scope with the stranger's.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"{route}?accountId={world.StrangerAccountId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A customer reading their own domain's DNS records is answered with them.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_domains_dns_records_is_answered_with_them()
    {
        // The inverse control for every refusal above, and it pins the zone-relative names on the wire.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/mail-domains/{world.Own.DomainId}/dns-records");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("name").GetString();
        }).ToList();

        Assert.Equal(["@", "@", "m20261107-1._domainkey", "_dmarc"], names);
    }

    /// <summary>An anonymous caller is refused by every administrator mail endpoint.</summary>
    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_administrator_mail_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, WorldIds.Unknown), "server");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every administrator mail endpoint and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_administrator_mail_endpoint(string method, string path)
    {
        // 403, and deliberately not 404: there is no tenant here and no identifier to probe, because there is
        // one mail stack on this machine and it is the server's. The specific code is asserted.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, WorldIds.Unknown), "server");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator reading the mail server status is answered with it.</summary>
    [Fact]
    public async Task An_administrator_reading_the_mail_server_status_is_answered_with_it()
    {
        // The inverse control for the role gate: a policy that refused EVERY caller would pass every theory
        // above while the surface was dead rather than protected. The body is read as well as the code.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/mail-server");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mail.example.com", body.RootElement.GetProperty("settings").GetProperty("hostname").GetString());
    }

    /// <summary>An administrator reading the mail queue count is answered with the figure.</summary>
    [Fact]
    public async Task An_administrator_reading_the_mail_queue_count_is_answered_with_the_figure()
    {
        // The inverse control for the role gate on the badge's route, read as a body: the figure is a
        // number the badge draws, and the simulated host's empty queue is a loaded zero, not a refusal.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/mail-queue/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("count").GetInt32());
    }

    /// <summary>Every resource scoped customer route is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the first time
    /// somebody adds a route. This reads the routes off the controllers themselves, so a new resource-scoped
    /// endpoint fails HERE, naming itself, rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_resource_scoped_customer_route_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<MailDomainsController>()
            .Concat(ControllerRoutes.Declared<MailboxesController>())
            .Concat(ControllerRoutes.Declared<MailboxAutoresponderController>())
            .Concat(ControllerRoutes.Declared<MailAliasesController>())
            .ToList();
        Assert.NotEmpty(declared);

        var covered = ResourceScopedEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {Generic((string)row[1])}";
            })
            .ToHashSet(StringComparer.Ordinal);
        var missing = declared
            .Where(route =>
            {
                return ControllerRoutes.IsResourceScoped(route);
            })
            .Where(route =>
            {
                return !covered.Contains(Generic(route));
            })
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These resource-scoped mail routes are absent from ResourceScopedEndpoints(), so nothing proves "
            + "they answer 404 rather than 403 for another tenant: " + string.Join(", ", missing));
    }

    /// <summary>Every server route is covered by the role fixture.</summary>
    [Fact]
    public void Every_server_route_is_covered_by_the_role_fixture()
    {
        var declared = ControllerRoutes.Declared<MailServerController>()
            .Concat(ControllerRoutes.Declared<MailQueueController>())
            .ToList();
        Assert.NotEmpty(declared);

        var covered = AdministratorEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {Generic((string)row[1])}";
            })
            .ToHashSet(StringComparer.Ordinal);
        var missing = declared.Where(route =>
        {
            return !covered.Contains(Generic(route));
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These administrator mail routes are absent from AdministratorEndpoints(), so nothing proves "
            + "they are closed to a customer: " + string.Join(", ", missing));
    }

    /// <summary>Rewrites every <c>{name}</c> in a route to <c>{}</c>, so two spellings of one route compare equal.</summary>
    /// <param name="route">A "METHOD /path" string or a path.</param>
    private static string Generic(string route)
    {
        return System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "{}");
    }

    /// <summary>Substitutes the seeded identifiers into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="ids">The identifiers to place in it.</param>
    private static string Substitute(string path, WorldIds ids)
    {
        return path
            .Replace("{domain}", ids.DomainId.ToString(), StringComparison.Ordinal)
            .Replace("{mailbox}", ids.MailboxId.ToString(), StringComparison.Ordinal)
            .Replace("{alias}", ids.AliasId.ToString(), StringComparison.Ordinal)
            .Replace("{queue}", "AAAAAAAAAA", StringComparison.Ordinal);
    }

    /// <summary>Reads the local parts of the mailboxes with an id, past the tenant filter.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="mailboxId">The mailbox to look for.</param>
    private static async Task<List<string>> MailboxLocalPartsAsync(
        WebApplicationFactory<Program> factory,
        Guid mailboxId)
    {
        using var scope = factory.Services.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailDbContext>();

        return await mail.Mailboxes
            .IgnoreQueryFilters()
            .Where(row => row.Id == mailboxId)
            .Select(row => row.LocalPart)
            .ToListAsync();
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent substituted.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMailClient>(new StubAgentMailClient());
            });
        });
    }

    /// <summary>Applies every module's migrations the test needs, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<MailDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, a mail server row, and a domain, mailbox and alias each.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var mail = scope.ServiceProvider.GetRequiredService<MailDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        mail.MailServerSettings.Add(new MailServerSettings(
            "mail.example.com", false, false, false, 50, 4, 6, 15, now));

        // Written through a context resolved OUTSIDE a request, whose ICurrentUser is not a signed-in
        // customer, so the seed itself does no tenant separating: the filter under test is the only thing
        // that can. Both accounts carry rows with the SAME local part, which the per-domain unique key
        // must accept.
        var ownIds = AddMailRows(mail, own.Id, "own.example.com", StrangerHash, now);
        var strangerIds = AddMailRows(mail, stranger.Id, "stranger.example.com", StrangerHash, now);
        await mail.SaveChangesAsync();

        return new SeededWorld(ownIds, strangerIds, stranger.Id);
    }

    /// <summary>Adds a domain with a key, a mailbox named <c>info</c> and an alias named <c>sales</c> for one account.</summary>
    /// <param name="mail">The Mail module's context.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domainName">The domain name.</param>
    /// <param name="hash">The mailbox's password hash.</param>
    /// <param name="now">The creation instant.</param>
    private static WorldIds AddMailRows(
        MailDbContext mail,
        Guid accountId,
        string domainName,
        string hash,
        DateTimeOffset now)
    {
        var domain = new MailDomain(Guid.NewGuid(), accountId, domainName, Guid.NewGuid(), now);
        domain.RotateDkim(new DkimKey(Guid.NewGuid(), domain.Id, "m20261107-1", "enc:key", "UFVCTElDS0VZ", now), now);
        var mailbox = new Mailbox(Guid.NewGuid(), accountId, domain.Id, "info", hash, 1024, 100, now);
        var alias = new MailAlias(
            Guid.NewGuid(), accountId, domain.Id, "sales", MailAliasKind.Alias, [$"info@{domainName}"], now);
        mail.MailDomains.Add(domain);
        mail.Mailboxes.Add(mailbox);
        mail.MailAliases.Add(alias);
        mail.MailAliasTargets.AddRange(alias.Targets);

        return new WorldIds(domain.Id, mailbox.Id, alias.Id);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving each route that has a body one its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make an
    /// authorization theory pass without the request ever reaching the gate or the handler whose tenant
    /// scoping is the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    /// <param name="body">Which body to send, by name; empty for none.</param>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        string path,
        string body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var content = BodyFor(body, path);
        if (content is not null)
        {
            request.Content = content;
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a valid request body by name.</summary>
    /// <param name="body">The body's name.</param>
    /// <param name="path">The absolute path, which decides the create bodies.</param>
    private static JsonContent? BodyFor(string body, string path)
    {
        return body switch
        {
            "mailbox-update" => JsonContent.Create(new { quotaMib = 512, sendLimitPerHour = 50, enabled = true }),
            "mailbox-password" => JsonContent.Create(new { password = "a perfectly fine password" }),
            "autoresponder" => JsonContent.Create(new { subject = "Away", body = "Back soon." }),
            "alias-update" => JsonContent.Create(new { targets = new List<string> { "someone@own.example.com" } }),
            "server" => JsonContent.Create(new
            {
                hostname = "mail.example.com",
                pop3Enabled = false,
                antivirusEnabled = false,
                allowExternalForwarding = false,
                maxMessageMib = 50,
                spamGreylistScore = 4,
                spamAddHeaderScore = 6,
                spamRejectScore = 15,
            }),
            "create" when path.EndsWith("/mail-domains", StringComparison.Ordinal) =>
                JsonContent.Create(new { accountId = Guid.NewGuid(), name = "new.example.com" }),
            "create" when path.EndsWith("/mailboxes", StringComparison.Ordinal) =>
                JsonContent.Create(new
                {
                    mailDomainId = Guid.NewGuid(),
                    localPart = "new",
                    password = "a perfectly fine password",
                    quotaMib = 512,
                }),
            "create" when path.EndsWith("/mail-aliases", StringComparison.Ordinal) =>
                JsonContent.Create(new
                {
                    mailDomainId = Guid.NewGuid(),
                    kind = "alias",
                    localPart = "new",
                    targets = new List<string> { "someone@example.com" },
                }),
            _ => null,
        };
    }

    /// <summary>The identifiers one account's seeded mail rows carry.</summary>
    /// <param name="DomainId">The domain.</param>
    /// <param name="MailboxId">The mailbox named info.</param>
    /// <param name="AliasId">The alias named sales.</param>
    private sealed record WorldIds(Guid DomainId, Guid MailboxId, Guid AliasId)
    {
        /// <summary>Identifiers that belong to nothing, for the unknown-id theory.</summary>
        public static WorldIds Unknown { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="Own">The signed-in customer's rows.</param>
    /// <param name="Stranger">The other tenant's rows.</param>
    /// <param name="StrangerAccountId">The other tenant's account, which a customer may name in a filter.</param>
    private sealed record SeededWorld(WorldIds Own, WorldIds Stranger, Guid StrangerAccountId);
}
