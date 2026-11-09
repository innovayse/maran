using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Whether the operator's alert mail is reaching its destination, read over real HTTP against real
/// PostgreSQL after real <see cref="SendMailRequested"/> messages have been handled (issues #142
/// and #164).
/// </summary>
/// <remarks>
/// <para>
/// The mailer is the one thing replaced, by an <see cref="OutcomeMailer"/> whose answer the test
/// chooses; the handler, the state recorder, the database and the query are the shipped ones. The
/// message is handled with <c>InvokeAsync</c>, which runs the handler inline, so the assertion
/// reads a state that is already written and no test waits on a queue.
/// </para>
/// <para>
/// Each failing case has its inverse control in the same class: the same failing mailer driven by a
/// message that is NOT an operator alert, and by a panel with no mail settings, must leave the state
/// unknown. Without them a recorder that marked every failure of every mail as an alert failure
/// would pass.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AlertDeliveryStatusEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/notifications/alert-delivery";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mailer the host under test sends through; each test sets its answer.</summary>
    private readonly OutcomeMailer _mailer = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AlertDeliveryStatusEndpointTests(PostgresFixture postgres)
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

    /// <summary>A panel that has sent no alert reports both channels as unknown.</summary>
    [Fact]
    public async Task A_panel_that_has_sent_no_alert_reports_both_channels_as_unknown()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));

        Assert.Equal("unknown", body.RootElement.GetProperty("mail").GetProperty("state").GetString());
        Assert.Equal("unknown", body.RootElement.GetProperty("webhook").GetProperty("state").GetString());
    }

    /// <summary>An operator alert the mail server refused reports the mail channel as failing, with when and in words.</summary>
    [Fact]
    public async Task An_operator_alert_the_mail_server_refused_reports_the_mail_channel_as_failing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        _mailer.RefuseDelivery();

        await HandleMailAsync(factory, MailPurpose.OperatorAlert);

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        var mail = body.RootElement.GetProperty("mail");
        Assert.Equal("failing", mail.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, mail.GetProperty("lastFailureAt").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(mail.GetProperty("failureMessage").GetString()));
        Assert.Equal("unknown", body.RootElement.GetProperty("webhook").GetProperty("state").GetString());
    }

    /// <summary>An operator alert that was delivered reports the mail channel as ok, with no failure to show.</summary>
    [Fact]
    public async Task An_operator_alert_that_was_delivered_reports_the_mail_channel_as_ok()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        _mailer.Deliver();

        await HandleMailAsync(factory, MailPurpose.OperatorAlert);

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        var mail = body.RootElement.GetProperty("mail");
        Assert.Equal("ok", mail.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, mail.GetProperty("lastFailureAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, mail.GetProperty("failureMessage").ValueKind);
    }

    /// <summary>A delivery that works after one that failed brings the channel back to ok.</summary>
    [Fact]
    public async Task A_delivery_that_works_after_a_failure_brings_the_channel_back_to_ok()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        _mailer.RefuseDelivery();
        await HandleMailAsync(factory, MailPurpose.OperatorAlert);
        Assert.Equal("failing", await MailStateAsync(client));

        _mailer.Deliver();
        await HandleMailAsync(factory, MailPurpose.OperatorAlert);

        Assert.Equal("ok", await MailStateAsync(client));
    }

    /// <summary>The same refusal on a mail that is not an operator alert leaves the channel unknown.</summary>
    [Fact]
    public async Task The_same_refusal_on_a_mail_that_is_not_an_operator_alert_leaves_the_channel_unknown()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        _mailer.RefuseDelivery();

        await HandleMailAsync(factory, MailPurpose.Transactional);

        Assert.Equal("unknown", await MailStateAsync(client));
    }

    /// <summary>A panel with no mail settings skips the alert, which attempted nothing and so is not a failure.</summary>
    [Fact]
    public async Task A_panel_with_no_mail_settings_skips_the_alert_and_the_channel_stays_unknown()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        _mailer.LackSettings();

        await HandleMailAsync(factory, MailPurpose.OperatorAlert);

        Assert.Equal("unknown", await MailStateAsync(client));
    }

    /// <summary>A signed in customer cannot read the delivery state, and an administrator can.</summary>
    [Fact]
    public async Task A_signed_in_customer_cannot_read_the_delivery_state()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var customer = await SignInAsync(factory, "customer");
        using var administrator = await SignInAsync(factory, "admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync(Path)).StatusCode);
    }

    /// <summary>Hands one mail request to the shipped handler and waits for it to finish.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="purpose">Why the mail is sent.</param>
    /// <returns>Resolves once the handler has recorded its outcome.</returns>
    private static async Task HandleMailAsync(WebApplicationFactory<Program> factory, MailPurpose purpose)
    {
        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new SendMailRequested("operator@example.com", "Alert", "Body", purpose));
    }

    /// <summary>Reads the state of the mail channel.</summary>
    /// <param name="client">An administrator's client.</param>
    /// <returns>The channel's state word.</returns>
    private static async Task<string?> MailStateAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        return body.RootElement.GetProperty("mail").GetProperty("state").GetString();
    }

    /// <summary>Boots the host against this test's database with the mailer replaced.</summary>
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

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailer>(_mailer);
            });
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
