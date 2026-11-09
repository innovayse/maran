using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The administrators' notification feed over real HTTP against real PostgreSQL (issues #142 and
/// #164): the list, the badge count, and dismissal, which belongs to the administrator who did it.
/// </summary>
/// <remarks>
/// <para>
/// Two administrators are signed in side by side in every dismissal test, because the property
/// worth proving is a difference between them: one clearing the bell must leave it full for the
/// other. A test with a single administrator cannot tell a per-administrator dismissal from a
/// global one.
/// </para>
/// <para>
/// Each refusal has an inverse control (a dismissal that DOES write, a customer's refusal beside an
/// administrator's answer), and each asserts the stored rows and the journal, not only the status.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class NotificationFeedEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password every seeded user is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The feed's route.</summary>
    private const string Feed = "/api/v1/notifications/feed";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public NotificationFeedEndpointTests(PostgresFixture postgres)
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

    /// <summary>The list shows every entry newest first with the total, and the count agrees with it.</summary>
    [Fact]
    public async Task The_list_shows_every_entry_newest_first_and_the_count_agrees()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var oldest = await SeedAsync(factory, NotificationSource.Task, "oldest", daysAgo: 3);
        var newest = await SeedAsync(factory, NotificationSource.Alert, "newest", daysAgo: 1);
        var middle = await SeedAsync(factory, NotificationSource.Backup, "middle", daysAgo: 2);
        using var client = await SignInAsync(factory, "admin");

        Assert.Equal([newest, middle, oldest], await ListIdsAsync(client));
        Assert.Equal((3, false), await CountAsync(client));
    }

    /// <summary>The source filter narrows the list to one source; the unfiltered list is the inverse control.</summary>
    [Fact]
    public async Task The_source_filter_narrows_the_list_to_one_source()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        await SeedAsync(factory, NotificationSource.Task, "task", daysAgo: 3);
        var alert = await SeedAsync(factory, NotificationSource.Alert, "alert", daysAgo: 2);
        await SeedAsync(factory, NotificationSource.Backup, "backup", daysAgo: 1);
        using var client = await SignInAsync(factory, "admin");

        Assert.Equal([alert], await ListIdsAsync(client, "?source=Alert"));
        Assert.Equal(3, (await ListIdsAsync(client)).Length);
    }

    /// <summary>The search matches the subject case-insensitively and a literal percent sign matches nothing.</summary>
    [Fact]
    public async Task The_search_matches_the_subject_and_a_percent_sign_is_literal()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var shop = await SeedAsync(factory, NotificationSource.Alert, "shop.example.com", daysAgo: 2);
        await SeedAsync(factory, NotificationSource.Alert, "blog.example.org", daysAgo: 1);
        using var client = await SignInAsync(factory, "admin");

        Assert.Equal([shop], await ListIdsAsync(client, "?search=shop"));
        Assert.Equal([shop], await ListIdsAsync(client, "?search=SHOP"));
        Assert.Empty(await ListIdsAsync(client, "?search=%25"));
        Assert.Equal(2, (await ListIdsAsync(client)).Length);
    }

    /// <summary>The badge stops at ninety-nine with the capped flag, and exactly ninety-nine is not capped.</summary>
    [Fact]
    public async Task The_count_stops_at_ninety_nine_and_says_so_only_beyond_it()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        await SeedManyAsync(factory, 99);
        using var client = await SignInAsync(factory, "admin");

        Assert.Equal((99, false), await CountAsync(client));

        await SeedManyAsync(factory, 1);

        Assert.Equal((99, true), await CountAsync(client));
    }

    /// <summary>One administrator's dismissal hides that entry for them only, and is journalled once.</summary>
    [Fact]
    public async Task One_administrators_dismissal_does_not_hide_the_entry_for_another()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var first = await SeedAsync(factory, NotificationSource.Alert, "first", daysAgo: 2);
        var second = await SeedAsync(factory, NotificationSource.Alert, "second", daysAgo: 1);
        using var admin = await SignInAsync(factory, "admin");
        using var other = await SignInAsync(factory, "other");

        var response = await admin.PostAsync($"{Feed}/{first}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([second], await ListIdsAsync(admin));
        Assert.Equal((1, false), await CountAsync(admin));
        Assert.Equal([second, first], await ListIdsAsync(other));
        Assert.Equal((2, false), await CountAsync(other));

        var entry = Assert.Single(await DismissedEntriesAsync(factory));
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("1", entry.Subject);
    }

    /// <summary>Dismissing an entry twice, or one that does not exist, answers 204 and writes and journals nothing more.</summary>
    [Fact]
    public async Task Dismissing_twice_or_an_unknown_entry_is_a_no_op_that_writes_nothing_more()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var only = await SeedAsync(factory, NotificationSource.Task, "only", daysAgo: 1);
        using var admin = await SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Feed}/{only}/dismiss", null)).StatusCode);
        Assert.Single(await DismissedEntriesAsync(factory));

        var again = await admin.PostAsync($"{Feed}/{only}/dismiss", content: null);
        var unknown = await admin.PostAsync($"{Feed}/{Guid.NewGuid()}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Single(await DismissedEntriesAsync(factory));
        Assert.Equal(1, await DismissalCountAsync(factory));
    }

    /// <summary>Dismiss-all clears the bell for the caller alone, writes one row per entry for them and journals the number.</summary>
    [Fact]
    public async Task Dismiss_all_clears_the_bell_for_the_caller_alone()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        await SeedManyAsync(factory, 3);
        using var admin = await SignInAsync(factory, "admin");
        using var other = await SignInAsync(factory, "other");

        var response = await admin.PostAsync($"{Feed}/dismiss-all", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ListIdsAsync(admin));
        Assert.Equal((0, false), await CountAsync(admin));
        Assert.Equal(3, (await ListIdsAsync(other)).Length);
        Assert.Equal((3, false), await CountAsync(other));
        Assert.Equal(3, await DismissalCountAsync(factory));

        var entry = Assert.Single(await DismissedEntriesAsync(factory));
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("3", entry.Subject);
    }

    /// <summary>Dismiss-all with nothing left to dismiss answers 204 and journals nothing.</summary>
    [Fact]
    public async Task Dismiss_all_with_nothing_to_dismiss_journals_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var admin = await SignInAsync(factory, "admin");

        var response = await admin.PostAsync($"{Feed}/dismiss-all", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await DismissedEntriesAsync(factory));
    }

    /// <summary>A signed in customer is refused every feed route and dismisses nothing; an administrator is answered.</summary>
    [Fact]
    public async Task A_signed_in_customer_is_refused_every_feed_route_and_dismisses_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var entry = await SeedAsync(factory, NotificationSource.Alert, "entry", daysAgo: 1);
        using var customer = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Feed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"{Feed}/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync($"{Feed}/{entry}/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync($"{Feed}/dismiss-all", null)).StatusCode);
        Assert.Equal(0, await DismissalCountAsync(factory));

        using var administrator = await SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync(Feed)).StatusCode);
    }

    /// <summary>Reads the ids of the first page of the list.</summary>
    /// <param name="client">The caller's client.</param>
    /// <param name="query">An optional query string, with its leading question mark.</param>
    /// <returns>The ids, in the order the list returned them.</returns>
    private static async Task<Guid[]> ListIdsAsync(HttpClient client, string query = "")
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(Feed + query));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(element =>
            {
                return element.GetProperty("id").GetGuid();
            })
            .ToArray();
    }

    /// <summary>Reads the badge figure.</summary>
    /// <param name="client">The caller's client.</param>
    /// <returns>The count and whether it was capped.</returns>
    private static async Task<(int Count, bool Capped)> CountAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync($"{Feed}/unread-count"));
        return (body.RootElement.GetProperty("count").GetInt32(), body.RootElement.GetProperty("capped").GetBoolean());
    }

    /// <summary>Reads the journal entries dismissals wrote.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The entries, oldest first.</returns>
    private static async Task<IReadOnlyList<AuditEvent>> DismissedEntriesAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == OutgoingMailAuditActions.NotificationsDismissed)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Counts the dismissal rows stored, for every administrator.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The number of rows.</returns>
    private static async Task<int> DismissalCountAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .NotificationDismissals.CountAsync();
    }

    /// <summary>Stores one entry that happened the given number of days ago.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="source">Where the entry came from.</param>
    /// <param name="subject">What it is about.</param>
    /// <param name="daysAgo">How long before now it happened.</param>
    /// <returns>The entry's id.</returns>
    private static async Task<Guid> SeedAsync(
        WebApplicationFactory<Program> factory,
        NotificationSource source,
        string subject,
        int daysAgo)
    {
        using var scope = factory.Services.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var notification = new PanelNotification(
            Guid.NewGuid(),
            source,
            "Test",
            NotificationSeverity.Warning,
            subject,
            PanelNotificationArguments.None,
            deepLink: null,
            now.AddDays(-daysAgo));

        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        dbContext.PanelNotifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return notification.Id;
    }

    /// <summary>Stores <paramref name="count"/> recent entries in one write.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="count">How many entries to store.</param>
    private static async Task SeedManyAsync(WebApplicationFactory<Program> factory, int count)
    {
        using var scope = factory.Services.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        for (var index = 0; index < count; index++)
        {
            dbContext.PanelNotifications.Add(new PanelNotification(
                Guid.NewGuid(),
                NotificationSource.Alert,
                "Test",
                NotificationSeverity.Info,
                $"bulk-{index}",
                PanelNotificationArguments.None,
                deepLink: null,
                now.AddMinutes(-index)));
        }

        await dbContext.SaveChangesAsync();
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

    /// <summary>Seeds two administrators and one customer.</summary>
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
            Guid.NewGuid(), "other", "other@example.com", hasher.Hash(Password), UserRole.Admin, now));
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
