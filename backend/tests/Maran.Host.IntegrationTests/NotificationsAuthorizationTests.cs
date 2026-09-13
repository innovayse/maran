using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Controllers;
using Maran.Modules.Notifications.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel's outgoing-mail surface over real HTTP against real PostgreSQL: who may reach it, and
/// what the SMTP settings hand back once they have been saved.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a ROLE, not ownership. There is no tenant dimension here at all: a panel has one mail
/// server, and its credential belongs to the operator. An anonymous caller is answered 401 and a
/// signed-in customer 403 — the admin-gating idiom the rest of the panel's server-wide surfaces use.
/// 404 would be the wrong answer: it is the TENANT answer, and using it here would tell a caller a
/// resource "does not exist" when they are simply not an administrator.
/// </para>
/// <para>
/// Nothing is substituted. This surface reaches no agent — mail leaves over SMTP, which needs no root
/// — so the host boots as it ships.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class NotificationsAuthorizationTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Every route the outgoing-mail surface declares.</summary>
    /// <remarks>
    /// Completeness is asserted by <see cref="Every_mail_route_is_covered_by_the_gating_fixture"/>
    /// rather than trusted: a new route added without a row here would otherwise enjoy no proof that
    /// it is closed to a customer at all.
    /// </remarks>
    /// <returns>The method and path of every gated route.</returns>
    public static TheoryData<string, string> MailEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/notifications/smtp" },
            { "PUT", "/api/v1/notifications/smtp" },
            { "POST", "/api/v1/notifications/smtp/test" },
        };
    }

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public NotificationsAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every mail endpoint.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [MemberData(nameof(MailEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_mail_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every mail endpoint and told so plainly.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [MemberData(nameof(MailEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_mail_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403 and deliberately not 404. The tenant rule — another customer's row answers "not found"
        // — exists so an identifier cannot be used as an oracle. There is no tenant here and no
        // identifier to probe: the settings are the operator's, and a customer is simply not an
        // administrator.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Saved mail settings are read back without the password, and in the casing they were sent in.</summary>
    /// <remarks>
    /// <para>
    /// The unit test asserts the read model has nowhere to put a password; this asserts the same thing
    /// about the bytes that actually leave the process, which is where a serializer setting or a
    /// future field would show up.
    /// </para>
    /// <para>
    /// The security level is sent as the name the panel's converter binds and asserted back verbatim,
    /// because the defect this pins was a field disagreeing with itself across one round trip: the
    /// handler projected <c>Security.ToString()</c>, so the read answered <c>StartTls</c> while the
    /// PUT beside it accepted <c>startTls</c>, and a client could not send back what it had just
    /// received. Restoring that <c>ToString()</c> in <c>GetSmtpSettingsQueryHandler</c> — or widening
    /// <c>SmtpSettingsDto.Security</c> back to <c>string</c> — reddens this test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Saved_mail_settings_are_read_back_without_the_password()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var saved = await client.PutAsJsonAsync("/api/v1/notifications/smtp", new
        {
            host = "smtp.example.com",
            port = 587,
            security = "startTls",
            username = "panel",
            password = "hunter2",
            fromAddress = "panel@example.com",
            fromName = "Maran Panel",
            alertRecipient = "ops@example.com",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var response = await client.GetAsync("/api/v1/notifications/smtp");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("hunter2", payload, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(payload);
        Assert.True(body.RootElement.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("smtp.example.com", body.RootElement.GetProperty("host").GetString());

        // Exactly what was sent, character for character: the round trip is the contract.
        Assert.Equal("startTls", body.RootElement.GetProperty("security").GetString());
    }

    /// <summary>Mail settings whose sender name carries a newline are refused as the caller's mistake.</summary>
    /// <remarks>
    /// rules/security.md item 4 at the HTTP boundary: an embedded CRLF in a header-bound value does
    /// not corrupt one header, it invents the next one.
    /// </remarks>
    [Fact]
    public async Task Mail_settings_whose_sender_name_carries_a_newline_are_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync("/api/v1/notifications/smtp", new
        {
            host = "smtp.example.com",
            port = 587,
            security = 1,
            username = "panel",
            password = "hunter2",
            fromAddress = "panel@example.com",
            fromName = "Panel\r\nBcc: attacker@example.net",
            alertRecipient = "ops@example.com",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Saving the mail settings is journalled with the server and never with the credential.</summary>
    [Fact]
    public async Task Saving_the_mail_settings_is_journalled_with_the_server_and_never_the_credential()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        await client.PutAsJsonAsync("/api/v1/notifications/smtp", new
        {
            host = "smtp.example.com",
            port = 587,
            security = 1,
            username = "panel",
            password = "hunter2",
            fromAddress = "panel@example.com",
            fromName = "Maran Panel",
            alertRecipient = "ops@example.com",
        });

        var response = await client.GetAsync("/api/v1/audit");
        var payload = await response.Content.ReadAsStringAsync();

        using var body = JsonDocument.Parse(payload);
        var entries = body.RootElement.EnumerateArray()
            .Select(entry =>
            {
                return (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString());
            })
            .ToList();

        Assert.Contains(("SmtpSettingsSaved", "smtp.example.com"), entries);
        Assert.DoesNotContain("hunter2", payload, StringComparison.Ordinal);
    }

    /// <summary>A test message on a panel with no mail settings is refused with the reason.</summary>
    [Fact]
    public async Task A_test_message_on_a_panel_with_no_mail_settings_is_refused_with_the_reason()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/notifications/smtp/test", new { recipient = "ops@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SmtpNotConfigured", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Every mail route is covered by the gating fixture.</summary>
    [Fact]
    public void Every_mail_route_is_covered_by_the_gating_fixture()
    {
        // A hand-written list of routes goes stale the first time somebody adds one. These are read
        // off the controller itself, so a new endpoint fails HERE — naming itself — rather than
        // quietly enjoying no proof that it is closed to a customer.
        var declared = ControllerRoutes.Declared<SmtpSettingsController>().ToList();
        Assert.NotEmpty(declared);

        var inFixture = MailEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These mail routes are absent from MailEndpoints(), so nothing proves they "
            + "are closed to a signed-in customer: " + string.Join(", ", missing));
    }

    /// <summary>Issues one request, giving each body-taking route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make a
    /// gating theory pass without the request ever reaching the authorization policy under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT" or "POST")
        {
            request.Content = BodyFor(path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a valid request body for one body-taking route.</summary>
    /// <param name="path">The absolute path being written to.</param>
    /// <returns>The body.</returns>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/smtp/test", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { recipient = "ops@example.com" });
        }

        return JsonContent.Create(new
        {
            host = "smtp.example.com",
            port = 587,
            security = 1,
            username = "panel",
            password = "hunter2",
            fromAddress = "panel@example.com",
            fromName = "Maran Panel",
            alertRecipient = "ops@example.com",
        });
    }


    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <returns>The booted host factory.</returns>
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
