using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Files.Controllers;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the file manager is required to have (rules/testing.md, Definition of Done 3), driven
/// over real HTTP against real PostgreSQL: two customers who each own one account, and the question asked of
/// every customer-reachable Files route at once — does a customer naming another tenant's account get 404,
/// and not 403.
/// </summary>
/// <remarks>
/// <b>The boundary under test has no row to hide.</b> The Files module keeps no table: every route names an
/// ACCOUNT, and what makes another tenant's account answer 404 is <c>IAccountDirectory</c> answering null
/// inside the account gate every handler calls first — a resolution, not a query filter, which
/// <c>TenantScopeTests</c> has nothing to walk for. This walk is therefore the only mechanical guard that
/// a route consults it, and it asserts two things, not one: that the answer is 404 rather than 403 or 200,
/// and that the AGENT was never addressed at all. The second matters because the failure this module could
/// have is not "returned a row it should not have" but "listed, wrote or deleted in somebody else's home",
/// which no response body would reveal.
///
/// Every refusal is paired with a positive control: the same routes, the same bodies, the caller's OWN
/// account, and the agent seen to be addressed as that account. Without it "not found" is true of every
/// request ever made, including one a broken route never handled.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FilesAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The route prefix every Files controller shares.</summary>
    private const string Prefix = "/api/v1/files";

    /// <summary>The system user name of the account the signed-in customer owns.</summary>
    private const string OwnUsername = "accountb";

    /// <summary>Paths a customer must never be able to make the panel forward: traversal, absolute, hostile and bounds.</summary>
    private static readonly string[] HostilePaths =
    [
        "..",
        "../etc/passwd",
        "a/../../b",
        "/etc/passwd",
        "a\\..\\b",
        "a\0b",
        "a//b",
        "a/./b",
        ".maran-trash/entries",
        new string('x', 256),
    ];

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FilesAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>Every customer-reachable Files route that names an account.</summary>
    /// <remarks>
    /// The options route is the one customer-reachable Files route missing from this list, because it
    /// names no account and returns the panel's limits; <see cref="Every_files_route_on_every_controller_is_covered_by_the_idor_walk"/>
    /// states that exemption in code rather than leaving it to this sentence.
    ///
    /// A plain list walked inside one test, rather than <c>[Theory]</c> rows, for the reason the cron fixture
    /// records: every test instance takes its own database and its own connection pool, and nineteen routes
    /// across several theories exhausts the server's connections. The assertion helper still names every
    /// route that answered wrongly, not only the first.
    /// </remarks>
    /// <returns>The method and route of every Files endpoint that names an account.</returns>
    public static IReadOnlyList<(string Method, string Path)> Endpoints()
    {
        return
        [
            ("GET", $"{Prefix}/entries"),
            ("GET", $"{Prefix}/entries/info"),
            ("GET", $"{Prefix}/search"),
            ("GET", $"{Prefix}/content"),
            ("GET", $"{Prefix}/download"),
            ("GET", $"{Prefix}/trash"),
            ("PUT", $"{Prefix}/content"),
            ("POST", $"{Prefix}/upload"),
            ("POST", $"{Prefix}/entries"),
            ("POST", $"{Prefix}/rename"),
            ("POST", $"{Prefix}/move"),
            ("POST", $"{Prefix}/copy"),
            ("POST", $"{Prefix}/permissions"),
            ("POST", $"{Prefix}/trash"),
            ("POST", $"{Prefix}/trash/restore"),
            ("POST", $"{Prefix}/delete"),
            ("POST", $"{Prefix}/archive"),
            ("POST", $"{Prefix}/extract"),
            ("DELETE", $"{Prefix}/trash"),
        ];
    }

    /// <summary>An anonymous caller is refused by every Files endpoint.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_every_files_endpoint()
    {
        var agent = new StubAgentFilesClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        await AssertEveryEndpointAsync(client, Guid.NewGuid(), HttpStatusCode.Unauthorized);

        Assert.Empty(agent.AddressedAccounts);
    }

    /// <summary>A customer naming another tenants account is answered not found and never forbidden.</summary>
    [Fact]
    public async Task A_customer_naming_another_tenants_account_is_answered_not_found_and_never_forbidden()
    {
        var agent = new StubAgentFilesClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customerb");

        await AssertEveryEndpointAsync(client, world.OtherTenantsAccountId, HttpStatusCode.NotFound);

        // The half a status code cannot show: not one call reached the agent, so nothing in the other
        // tenant's home — or in anybody's — was listed, read, written, moved or removed before the refusal.
        Assert.Empty(agent.AddressedAccounts);
    }

    /// <summary>An account identifier nobody owns is answered not found rather than failing.</summary>
    [Fact]
    public async Task An_account_identifier_nobody_owns_is_answered_not_found_rather_than_failing()
    {
        // 404, never 500: an identifier the caller invented is answered by the gate's typed NotFound, and
        // it must be the SAME answer another tenant's account gets, or the difference is the oracle.
        var agent = new StubAgentFilesClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customerb");

        await AssertEveryEndpointAsync(client, Guid.NewGuid(), HttpStatusCode.NotFound);

        Assert.Empty(agent.AddressedAccounts);
    }

    /// <summary>A customer naming their own account reaches their own home on every route.</summary>
    [Fact]
    public async Task A_customer_naming_their_own_account_reaches_their_own_home_on_every_route()
    {
        // Guards every assertion above from passing for the wrong reason: if the routes were simply broken,
        // or the seed never ran, "not found" would be true of every request ever made. The control is the
        // agent's record, not the status: the stub cannot stream a file read, so those two routes end in a
        // busy refusal, which is not the not-found a foreign account gets.
        var agent = new StubAgentFilesClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customerb");

        var wrong = new List<string>();
        foreach (var (method, path) in Endpoints())
        {
            agent.Reset();
            using var request = RequestFor(method, path, world.OwnAccountId);
            var response = await client.SendAsync(request);

            var expected = path is $"{Prefix}/content" or $"{Prefix}/download" && method == "GET"
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            var addressed = agent.AddressedAccounts;
            if (response.StatusCode != expected
                || addressed.Count == 0
                || addressed.Any(account => { return account != OwnUsername; }))
            {
                wrong.Add($"{method} {path} answered {(int)response.StatusCode}, agent addressed [{string.Join(",", addressed)}]");
            }
        }

        Assert.True(
            wrong.Count == 0,
            $"These routes did not reach the agent as {OwnUsername} with the expected answer: " + string.Join("; ", wrong));
    }

    /// <summary>A hostile path is refused by validation on every route and never reaches the agent.</summary>
    [Fact]
    public async Task A_hostile_path_is_refused_on_every_route_and_never_reaches_the_agent()
    {
        var agent = new StubAgentFilesClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customerb");

        // The positive control comes first and uses the same request builder: the legal path reaches the
        // agent, so a hostile path answered 400 below is the path being refused and not the builder being broken.
        using (var control = RequestFor("GET", $"{Prefix}/entries", world.OwnAccountId))
        {
            var allowed = await client.SendAsync(control);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.NotEmpty(agent.AddressedAccounts);
        }

        var wrong = new List<string>();
        foreach (var (method, path) in Endpoints().Where(endpoint => { return TakesPath(endpoint.Method, endpoint.Path); }))
        {
            foreach (var hostile in HostilePaths)
            {
                agent.Reset();
                using var request = RequestFor(method, path, world.OwnAccountId, hostile);
                var response = await client.SendAsync(request);

                if (response.StatusCode != HttpStatusCode.BadRequest || agent.AddressedAccounts.Count != 0)
                {
                    wrong.Add(
                        $"{method} {path} with {JsonSerializer.Serialize(hostile)} answered {(int)response.StatusCode}, "
                        + $"agent addressed {agent.AddressedAccounts.Count} time(s)");
                }
            }
        }

        Assert.True(wrong.Count == 0, "These requests were not refused before the agent: " + string.Join("; ", wrong));
    }

    /// <summary>Every Files route on every Files controller is covered by the idor walk.</summary>
    /// <remarks>
    /// The walk above is a hand-written list, and a hand-written list of routes goes stale the first time
    /// somebody adds a route. This reads the routes off the controllers themselves, so a new Files endpoint
    /// fails HERE — naming itself — rather than quietly enjoying no IDOR coverage. The one exemption is the
    /// options route, which names no account.
    /// </remarks>
    [Fact]
    public void Every_files_route_on_every_controller_is_covered_by_the_idor_walk()
    {
        var declared = ControllerRoutes.Declared<FilesBrowseController>()
            .Concat(ControllerRoutes.Declared<FilesTransferController>())
            .Concat(ControllerRoutes.Declared<FilesEntriesController>())
            .Concat(ControllerRoutes.Declared<FilesTrashController>())
            .Concat(ControllerRoutes.Declared<FilesArchivesController>())
            .Concat(ControllerRoutes.Declared<FilesOptionsController>())
            .ToList();
        Assert.NotEmpty(declared);

        var inWalk = Endpoints()
            .Select(endpoint =>
            {
                return $"{endpoint.Method} {endpoint.Path}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inWalk.Contains(route) && route != $"GET {Prefix}/options";
        }).ToList();
        var stale = inWalk.Where(route =>
        {
            return !declared.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These Files routes are absent from Endpoints(), so nothing proves they answer 404 "
            + "rather than 403 for another tenant: " + string.Join(", ", missing));
        Assert.True(
            stale.Count == 0,
            "Endpoints() names routes no Files controller declares: " + string.Join(", ", stale));
    }

    /// <summary>Asserts that every Files endpoint answers one status for one account identifier.</summary>
    /// <remarks>
    /// Every route is tried before anything is asserted, and each wrong answer is reported with its own
    /// method and path. Failing on the first would hide the rest, and "one route answers 403" and "every
    /// route answers 403" are different defects that a first-failure assertion reports identically.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="accountId">The account to name on every route.</param>
    /// <param name="expected">The status every route must answer.</param>
    private static async Task AssertEveryEndpointAsync(HttpClient client, Guid accountId, HttpStatusCode expected)
    {
        var wrong = new List<string>();
        foreach (var (method, path) in Endpoints())
        {
            using var request = RequestFor(method, path, accountId);
            var response = await client.SendAsync(request);
            if (response.StatusCode != expected)
            {
                wrong.Add($"{method} {path} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            $"These routes did not answer {(int)expected}: " + string.Join("; ", wrong));
    }

    /// <summary>Tells whether a route carries a path (or a trash id) a customer chooses.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <returns><see langword="true"/> unless the route only names an account.</returns>
    private static bool TakesPath(string method, string path)
    {
        return !(path == $"{Prefix}/trash" && method is "GET" or "DELETE");
    }

    /// <summary>The legal path a route is sent when the test is not trying to break it.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <returns>A path the stub agent's home holds, or one an operation may create.</returns>
    private static string LegalPathOf(string method, string path)
    {
        if (path == $"{Prefix}/upload")
        {
            return "uploaded.bin";
        }

        if (path == $"{Prefix}/extract")
        {
            return StubAgentFilesClient.ExistingArchive;
        }

        if (path == $"{Prefix}/entries" && method == "POST")
        {
            return "created.txt";
        }

        return StubAgentFilesClient.ExistingFile;
    }

    /// <summary>Builds one request, carrying the account and the path the way that route carries them.</summary>
    /// <remarks>
    /// The account travels in the query string on a read and in the body on a write, which is the module's
    /// own convention rather than this helper's. The bodies must be VALID: a route whose body fails
    /// validation answers 400 and the request never reaches the handler whose account resolution is the
    /// thing under test.
    /// </remarks>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <param name="accountId">The account to name.</param>
    /// <param name="target">The path to send instead of the route's legal one.</param>
    /// <returns>The request, which the caller disposes.</returns>
    private static HttpRequestMessage RequestFor(string method, string path, Guid accountId, string? target = null)
    {
        var chosen = target ?? LegalPathOf(method, path);
        var query = $"?accountId={accountId}&path={Uri.EscapeDataString(chosen)}";
        var route = $"{method} {path}";

        if (route is $"GET {Prefix}/trash" or $"DELETE {Prefix}/trash")
        {
            return new HttpRequestMessage(new HttpMethod(method), $"{path}?accountId={accountId}");
        }

        if (method == "GET")
        {
            return new HttpRequestMessage(
                HttpMethod.Get, path == $"{Prefix}/search" ? $"{path}{query}&search=index" : $"{path}{query}");
        }

        if (route == $"POST {Prefix}/upload")
        {
            return new HttpRequestMessage(HttpMethod.Post, $"{path}{query}")
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5])
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") },
                },
            };
        }

        object body = route switch
        {
            $"PUT {Prefix}/content" => new { accountId, path = chosen, content = "hello" },
            $"POST {Prefix}/entries" => new { accountId, path = chosen, kind = 0 },
            $"POST {Prefix}/rename" => new { accountId, path = chosen, newName = "renamed.txt" },
            $"POST {Prefix}/move" => new { accountId, sourcePaths = new[] { chosen }, destinationDirectory = "docs" },
            $"POST {Prefix}/copy" => new { accountId, sourcePaths = new[] { chosen }, destinationDirectory = "docs" },
            $"POST {Prefix}/permissions" => new { accountId, path = chosen, mode = "644", recursive = false },
            $"POST {Prefix}/trash" => new { accountId, paths = new[] { chosen } },
            $"POST {Prefix}/trash/restore" => new { accountId, trashIds = new[] { target ?? StubAgentFilesClient.TrashId } },
            $"POST {Prefix}/delete" => new { accountId, paths = new[] { chosen }, confirm = true },
            $"POST {Prefix}/archive" => new
            {
                accountId,
                sourcePaths = new[] { chosen },
                destinationPath = target ?? "archive.zip",
                format = 0,
            },
            $"POST {Prefix}/extract" => new { accountId, archivePath = chosen, destinationDirectory = "docs" },
            _ => throw new InvalidOperationException($"No request body is defined for {route}."),
        };

        return new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body) };
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The files agent double every request is served by.</param>
    private WebApplicationFactory<Program> CreateFactory(IAgentFilesClient agent)
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

            // The walk sends a hundred requests from one caller; the limiter is not what is under test.
            builder.UseSetting("RateLimiting:FilesMaxRequests", "100000");

            // The panel refuses to boot without them, and a host that does not boot fails every test below
            // with an options error rather than with an answer about tenancy.
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            // The ONLY substitution: the agent is another process on a provisioned host and cannot be
            // present. Everything the panel itself does stays the shipped code.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(agent);
            });
        });
    }

    /// <summary>Applies every migration this fixture needs, the way the installer does before first boot.</summary>
    /// <remarks>Identity and Accounts only: the Files module has no context, its state is the agent's.</remarks>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, each owned by its own customer.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var accountA = new Account(Guid.NewGuid(), "accounta", "a.example.com", planId, now);
        var accountB = new Account(Guid.NewGuid(), OwnUsername, "b.example.com", planId, now);
        accounts.Accounts.AddRange(accountA, accountB);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customerA = new User(
            Guid.NewGuid(), "customera", "customera@example.com", hasher.Hash(Password), UserRole.Customer, now);
        var customerB = new User(
            Guid.NewGuid(), "customerb", "customerb@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customerA.Id, accountA.Id, now));
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customerB.Id, accountB.Id, now));
        identity.Users.AddRange(customerA, customerB);
        await identity.SaveChangesAsync();

        return new SeededWorld(accountB.Id, accountA.Id);
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

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnAccountId">The account belonging to the signed-in customer.</param>
    /// <param name="OtherTenantsAccountId">The account belonging to the other customer.</param>
    private sealed record SeededWorld(Guid OwnAccountId, Guid OtherTenantsAccountId);
}
