using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The alert recipient list over real HTTP against real PostgreSQL (issues #142 and #164): read and
/// replaced as a whole, capped by the maximum the read hands out, free of duplicates, allowed to be
/// empty, and journalled.
/// </summary>
/// <remarks>
/// Every refusal is paired with an inverse control that must be ACCEPTED, and every refusal also
/// asserts that the stored list and the journal are untouched, because a refusal that still wrote
/// would pass a status-only assertion.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AlertRecipientsEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/notifications/alert-recipients";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AlertRecipientsEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A panel nobody configured reads back an empty list and the maximum.</summary>
    [Fact]
    public async Task A_panel_nobody_configured_reads_back_an_empty_list_and_the_maximum()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));

        Assert.Equal(0, body.RootElement.GetProperty("addresses").GetArrayLength());
        Assert.True(body.RootElement.GetProperty("maximum").GetInt32() > 0);
    }

    /// <summary>A saved list is read back in the order sent and lower-cased, and the save is journalled with its size.</summary>
    [Fact]
    public async Task A_saved_list_is_read_back_in_order_lower_cased_and_journalled_with_its_size()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync(
            Path, new { Addresses = new List<string> { "Ops@Example.com", "second@example.com" } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["ops@example.com", "second@example.com"], await ReadAddressesAsync(client));

        var entry = Assert.Single(await SavedEntriesAsync(factory));
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("2", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second save replaces the list: it reorders what stays, drops what is gone and adds what is new.</summary>
    [Fact]
    public async Task A_second_save_replaces_the_list_reordering_dropping_and_adding()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        await client.PutAsJsonAsync(Path, new { Addresses = new List<string> { "a@example.com", "b@example.com" } });

        var response = await client.PutAsJsonAsync(
            Path, new { Addresses = new List<string> { "c@example.com", "b@example.com" } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["c@example.com", "b@example.com"], await ReadAddressesAsync(client));

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .AlertRecipients.AsNoTracking().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, (await SavedEntriesAsync(factory)).Count);
    }

    /// <summary>An empty list is accepted, empties the stored list and is journalled as zero addresses.</summary>
    [Fact]
    public async Task An_empty_list_is_accepted_empties_the_stored_list_and_is_journalled_as_zero()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        await client.PutAsJsonAsync(Path, new { Addresses = new List<string> { "a@example.com" } });

        var response = await client.PutAsJsonAsync(Path, new { Addresses = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ReadAddressesAsync(client));

        var entries = await SavedEntriesAsync(factory);
        Assert.Contains(entries, entry =>
        {
            return entry.Subject == "0";
        });
    }

    /// <summary>A list at exactly the maximum is accepted, and one address more is refused with the module's code and changes nothing.</summary>
    [Fact]
    public async Task A_list_at_the_maximum_is_accepted_and_one_more_is_refused_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        // The cap is read from the endpoint, never restated here: the figure the SPA shows and the
        // figure the server enforces are one number, and this test is what keeps them one.
        using var read = JsonDocument.Parse(await client.GetStringAsync(Path));
        var maximum = read.RootElement.GetProperty("maximum").GetInt32();

        var accepted = await client.PutAsJsonAsync(Path, new { Addresses = Addresses(maximum) });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(maximum, (await ReadAddressesAsync(client)).Length);
        var journalled = (await SavedEntriesAsync(factory)).Count;

        var refused = await client.PutAsJsonAsync(Path, new { Addresses = Addresses(maximum + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("AlertRecipientsTooMany", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(Addresses(maximum), await ReadAddressesAsync(client));
        Assert.Equal(journalled, (await SavedEntriesAsync(factory)).Count);
    }

    /// <summary>A list naming one address twice, whatever its case or spacing, is refused and changes nothing; distinct addresses are the accepted control.</summary>
    [Fact]
    public async Task A_duplicate_address_is_refused_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var control = await client.PutAsJsonAsync(
            Path, new { Addresses = new List<string> { "a@example.com", "b@example.com" } });
        Assert.Equal(HttpStatusCode.NoContent, control.StatusCode);
        var journalled = (await SavedEntriesAsync(factory)).Count;

        var refused = await client.PutAsJsonAsync(
            Path, new { Addresses = new List<string> { "x@example.com", "X@Example.com" } });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("AlertRecipientDuplicate", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(["a@example.com", "b@example.com"], await ReadAddressesAsync(client));
        Assert.Equal(journalled, (await SavedEntriesAsync(factory)).Count);
    }

    /// <summary>An entry that is not an e-mail address is refused and changes nothing.</summary>
    [Fact]
    public async Task An_entry_that_is_not_an_address_is_refused_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var refused = await client.PutAsJsonAsync(
            Path, new { Addresses = new List<string> { "a@example.com", "not-an-address" } });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("AlertRecipientInvalid", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await ReadAddressesAsync(client));
        Assert.Empty(await SavedEntriesAsync(factory));
    }

    /// <summary>A signed in customer can neither read nor replace the list, and nothing is stored.</summary>
    [Fact]
    public async Task A_signed_in_customer_can_neither_read_nor_replace_the_list()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var customer = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Path)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await customer.PutAsJsonAsync(Path, new { Addresses = new List<string> { "a@example.com" } })).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .AlertRecipients.ToListAsync());

        using var administrator = await SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync(Path)).StatusCode);
    }

    /// <summary>Builds <paramref name="count"/> distinct addresses.</summary>
    /// <param name="count">How many to build.</param>
    /// <returns>The addresses, in order.</returns>
    private static string[] Addresses(int count)
    {
        return Enumerable.Range(0, count).Select(index =>
        {
            return $"person{index}@example.com";
        }).ToArray();
    }

    /// <summary>Reads the list the way the SPA does.</summary>
    /// <param name="client">An administrator's client.</param>
    /// <returns>The addresses, in stored order.</returns>
    private static async Task<string[]> ReadAddressesAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        return body.RootElement.GetProperty("addresses").EnumerateArray()
            .Select(element =>
            {
                return element.GetString()!;
            })
            .ToArray();
    }

    /// <summary>Reads the journal entries a save of the list wrote.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The entries, oldest first.</returns>
    private static async Task<IReadOnlyList<AuditEvent>> SavedEntriesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == OutgoingMailAuditActions.AlertRecipientsSaved)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Boots the host against this test's database.</summary>
    /// <returns>The host factory.</returns>
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
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>A client whose requests carry the user's bearer token.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
