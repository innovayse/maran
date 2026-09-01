using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the Accounts surface is required to have: every account-scoped endpoint is
/// named once, in <see cref="AccountScopedEndpoints"/>, and then put through the same three
/// questions — is it closed to a stranger, is it closed to a signed-in customer, and does it
/// answer an identifier the caller has no business knowing without saying whether it exists.
/// </summary>
/// <remarks>
/// The enumeration is the point. A per-endpoint test proves whatever its author remembered to
/// write; a theory over one list proves the same three properties of every row, and a new endpoint
/// that forgets to add its row is visible in review as a route with no entry here. `Accounts` are
/// the tenants themselves and the module is <c>[Authorize(AdminOnly)]</c> as a whole, so "another
/// tenant's identifier" means two distinct things, and both are asked: a customer reaching for any
/// account at all, and an administrator reaching for an account that is not there.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountsAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>
    /// Every endpoint under <c>/api/v1/accounts</c> that names an account, plus the two collection
    /// routes. <c>{id}</c> is substituted per test. Adding a route to the controller without adding
    /// it here leaves it unproven, which is what makes an omission reviewable.
    /// </summary>
    public static TheoryData<string, string> AccountScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/accounts" },
            { "POST", "/api/v1/accounts" },
            { "GET", "/api/v1/accounts/{id}" },
            { "POST", "/api/v1/accounts/{id}/suspend" },
            { "POST", "/api/v1/accounts/{id}/reactivate" },
            { "DELETE", "/api/v1/accounts/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountsAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every account endpoint, and told nothing else.</summary>
    [Theory]
    [MemberData(nameof(AccountScopedEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_account_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer is refused by every account endpoint, including their own account.</summary>
    [Theory]
    [MemberData(nameof(AccountScopedEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_account_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (ownAccountId, _) = await SeedAccountsAsync(factory);
        await SeedUsersAsync(factory, ownAccountId);
        using var client = await SignInAsync(factory, "customer");

        // Their OWN account id, not a stranger's: a customer must not reach this surface even for
        // the account they belong to. Authorization here is by role, not by ownership, and a check
        // that only rejected other people's identifiers would pass while leaving the module open.
        var response = await SendAsync(client, method, path.Replace("{id}", ownAccountId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator asking for an account that does not exist gets a plain not found.</summary>
    [Theory]
    [InlineData("GET", "/api/v1/accounts/{id}")]
    [InlineData("POST", "/api/v1/accounts/{id}/suspend")]
    [InlineData("POST", "/api/v1/accounts/{id}/reactivate")]
    [InlineData("DELETE", "/api/v1/accounts/{id}")]
    public async Task An_unknown_account_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (ownAccountId, _) = await SeedAccountsAsync(factory);
        await SeedUsersAsync(factory, ownAccountId);
        using var client = await SignInAsync(factory, "admin");

        var response = await SendAsync(client, method, path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal));

        // 404, never 500: an identifier the caller invented must be answered by the handler's
        // typed NotFound, not by an unhandled failure that reveals the shape of what is behind it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
        });
    }

    /// <summary>Applies both modules' migrations, the way the installer does before first boot.</summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Creates two accounts on one plan, and returns the customer's and the stranger's ids.</summary>
    private static async Task<(Guid Own, Guid Stranger)> SeedAccountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        context.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5));

        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        context.Accounts.AddRange(own, stranger);
        await context.SaveChangesAsync();

        return (own.Id, stranger.Id);
    }

    /// <summary>Creates the administrator and a customer who owns <paramref name="accountId"/>.</summary>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        context.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));

        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(accountId);
        context.Users.Add(customer);

        await context.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/login",
            new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving the two POST routes the empty body they accept.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        return await client.SendAsync(request);
    }
}
