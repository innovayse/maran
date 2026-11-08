using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The personal-data export (issue #146) over real HTTP against a real PostgreSQL: an administrator
/// downloads what the panel holds about an account or a person, a subject that does not exist is a 404,
/// no secret column reaches the document, the export is journalled, and nobody else can ask for it.
/// </summary>
/// <remarks>
/// Threat note: docs/superpowers/notes/2026-11-07-audit-retention-erasure-threat-note.md, section 5.
/// Each test boots its own host, so the export rate limit (six an hour) is never reached here.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PersonalDataExportEndpointTests : IAsyncLifetime
{
    /// <summary>The TOTP secret this class plants on a login and then hunts for in the exports.</summary>
    private const string PlantedTotpSecret = "PLANTED-TOTP-SECRET-5d8e";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PersonalDataExportEndpointTests(PostgresFixture postgres)
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

    /// <summary>An account export carries the team, the journal rows stamped with the account, and a no-store attachment.</summary>
    [Fact]
    public async Task An_account_export_carries_the_team_and_its_journal_rows_as_a_no_store_attachment()
    {
        await using var factory = await BootAsync();
        var acme = await AccountIdAsync(factory, "acme");
        await AddAccountEntryAsync(factory, acme);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"/api/v1/privacy/accounts/{acme}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("maran-personal-data-account-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("account", root.GetProperty("subject").GetProperty("kind").GetString());
        Assert.Equal(acme, root.GetProperty("subject").GetProperty("id").GetGuid());
        var team = SectionRecords(root, "identity", "team");
        Assert.Equal("olive", Assert.Single(team).GetProperty("username").GetString());
        var entry = Assert.Single(root.GetProperty("auditEntries").EnumerateArray());
        Assert.Equal("SiteCreated", entry.GetProperty("action").GetString());
        Assert.Equal(acme, entry.GetProperty("accountId").GetGuid());
    }

    /// <summary>A user export carries the login, the sessions with address and client, and the user's journal rows.</summary>
    [Fact]
    public async Task A_user_export_carries_the_login_the_sessions_and_the_users_journal_rows()
    {
        await using var factory = await BootAsync();
        (await EndpointHarness.SignInAsync(factory, "olive")).Dispose();
        var olive = await UserIdAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"/api/v1/privacy/users/{olive}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("user", root.GetProperty("subject").GetProperty("kind").GetString());
        var login = Assert.Single(SectionRecords(root, "identity", "user"));
        Assert.Equal("olive", login.GetProperty("username").GetString());
        Assert.Equal("olive@example.com", login.GetProperty("email").GetString());
        Assert.NotEmpty(SectionRecords(root, "identity", "sessions"));
        Assert.Contains(root.GetProperty("auditEntries").EnumerateArray(), entry =>
        {
            return entry.GetProperty("action").GetString() == "LoginSucceeded"
                && entry.GetProperty("actorUserId").GetGuid() == olive;
        });
    }

    /// <summary>An id no account holds is a 404 carrying the privacy code, and so is an id no user holds.</summary>
    [Fact]
    public async Task A_subject_that_does_not_exist_is_a_404_with_the_privacy_code()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var account = await admin.GetAsync($"/api/v1/privacy/accounts/{Guid.NewGuid()}/export");
        var user = await admin.GetAsync($"/api/v1/privacy/users/{Guid.NewGuid()}/export");

        Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, user.StatusCode);
        using var accountProblem = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        using var userProblem = JsonDocument.Parse(await user.Content.ReadAsStringAsync());
        Assert.Equal("PrivacySubjectNotFound", accountProblem.RootElement.GetProperty("code").GetString());
        Assert.Equal("PrivacySubjectNotFound", userProblem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// Neither the password hash, the TOTP secret nor the refresh-token digest of the login appears in
    /// either export, while the values the export is meant to carry do.
    /// </summary>
    /// <remarks>
    /// The secrets are read back from the database after being planted, so the test hunts for the real
    /// stored values. Its probe is shown able to find one first: it must find the e-mail address the
    /// export does carry, and it must find a secret appended to the body on purpose.
    /// </remarks>
    [Fact]
    public async Task No_planted_secret_column_appears_in_an_account_or_a_user_export()
    {
        await using var factory = await BootAsync();
        (await EndpointHarness.SignInAsync(factory, "olive")).Dispose();
        var planted = await PlantSecretsAsync(factory, "olive");
        var acme = await AccountIdAsync(factory, "acme");
        var olive = await UserIdAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var accountBody = await admin.GetStringAsync($"/api/v1/privacy/accounts/{acme}/export");
        var userBody = await admin.GetStringAsync($"/api/v1/privacy/users/{olive}/export");

        Assert.Equal(3, planted.Distinct().Count());
        Assert.All(planted, secret =>
        {
            Assert.False(string.IsNullOrWhiteSpace(secret));
        });
        var accountText = TextOf(accountBody);
        var userText = TextOf(userBody);
        Assert.True(Leaks(userText, "olive@example.com"));
        foreach (var secret in planted)
        {
            // The probe's own proof: a document that DOES carry the secret, serialised the way the
            // export serialises, must be seen by it. Without this the probe could be blind to the
            // characters the writer escapes (a base64 digest holds + and /).
            Assert.True(Leaks(TextOf(JsonSerializer.Serialize(new { leaked = secret })), secret));
            Assert.False(Leaks(accountText, secret));
            Assert.False(Leaks(userText, secret));
        }
    }

    /// <summary>An export is journalled as the administrator's, naming the kind and the id; a refused one is not.</summary>
    [Fact]
    public async Task An_export_is_journalled_as_the_administrators_and_a_refused_one_is_not()
    {
        await using var factory = await BootAsync();
        var acme = await AccountIdAsync(factory, "acme");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await admin.GetAsync($"/api/v1/privacy/accounts/{Guid.NewGuid()}/export");
        Assert.Empty(await ExportEntriesAsync(factory));
        (await admin.GetAsync($"/api/v1/privacy/accounts/{acme}/export")).EnsureSuccessStatusCode();

        var entry = Assert.Single(await ExportEntriesAsync(factory));
        Assert.Equal($"Account {acme:D}", entry.Subject);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal(await UserIdAsync(factory, "admin"), entry.ActorUserId);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A customer is refused both exports, and so is a caller with no token.</summary>
    [Fact]
    public async Task A_customer_is_refused_and_an_anonymous_caller_is_not_let_in()
    {
        await using var factory = await BootAsync();
        var acme = await AccountIdAsync(factory, "acme");
        var olive = await UserIdAsync(factory, "olive");
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/v1/privacy/accounts/{acme}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/v1/privacy/users/{olive}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/privacy/accounts/{acme}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/privacy/users/{olive}/export")).StatusCode);
        Assert.Empty(await ExportEntriesAsync(factory));
    }

    /// <summary>An operator viewing as the account is refused both exports and nothing is journalled.</summary>
    [Fact]
    public async Task A_view_as_session_is_refused_both_exports()
    {
        await using var factory = await BootAsync();
        var acme = await AccountIdAsync(factory, "acme");
        var olive = await UserIdAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var viewing = await StartViewAsAsync(factory, admin, acme);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewing.GetAsync($"/api/v1/privacy/accounts/{acme}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewing.GetAsync($"/api/v1/privacy/users/{olive}/export")).StatusCode);
        Assert.Empty(await ExportEntriesAsync(factory));
    }

    /// <summary>Boots a host with an administrator, the account <c>acme</c> and its owner <c>olive</c>.</summary>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await MigrateProvidersAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", acme);

        return factory;
    }

    /// <summary>
    /// Applies the migrations of every module whose provider answers for an account. An account export asks
    /// each of them for its rows, and a table that was never created is a 500, so a test that exports an
    /// account needs the schemas the shared harness leaves out because most endpoints never read them.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateProvidersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CronDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<DnsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<FtpDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<MailDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<SslDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
    }

    /// <summary>Says whether <paramref name="text"/> contains <paramref name="needle"/>, case-sensitively.</summary>
    /// <param name="text">The decoded text searched, from <see cref="TextOf"/>.</param>
    /// <param name="needle">The value hunted for.</param>
    private static bool Leaks(string text, string needle)
    {
        return text.Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns every property name and string value of a JSON document, decoded, one per line. The raw
    /// body is not searched because the writer escapes characters such as <c>+</c>, so a secret that
    /// held one would not appear in it verbatim even when it leaked.
    /// </summary>
    /// <param name="json">The document.</param>
    private static string TextOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        var lines = new List<string>();
        Collect(document.RootElement, lines);

        return string.Join('\n', lines);
    }

    /// <summary>Appends the names and string values under <paramref name="element"/> to <paramref name="lines"/>.</summary>
    /// <param name="element">The element to walk.</param>
    /// <param name="lines">Receives the decoded text.</param>
    private static void Collect(JsonElement element, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    lines.Add(property.Name);
                    Collect(property.Value, lines);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, lines);
                }

                break;
            case JsonValueKind.String:
                lines.Add(element.GetString()!);
                break;
        }
    }

    /// <summary>Returns the records of one section of an export document.</summary>
    /// <param name="root">The document's root.</param>
    /// <param name="module">The section's module.</param>
    /// <param name="name">The section's name.</param>
    private static List<JsonElement> SectionRecords(JsonElement root, string module, string name)
    {
        var section = root.GetProperty("sections").EnumerateArray().Single(candidate =>
        {
            return candidate.GetProperty("module").GetString() == module
                && candidate.GetProperty("name").GetString() == name;
        });

        return [.. section.GetProperty("records").EnumerateArray()];
    }

    /// <summary>Reads an account's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    private static async Task<Guid> AccountIdAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Maran.Modules.Accounts.Persistence.AccountsDbContext>();

        return await context.Accounts.IgnoreQueryFilters()
            .Where(account => account.Name == accountName)
            .Select(account => account.Id)
            .SingleAsync();
    }

    /// <summary>Reads a login's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    private static async Task<Guid> UserIdAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Users.Where(user => user.Username == username).Select(user => user.Id).SingleAsync();
    }

    /// <summary>Stores a journal row stamped with the account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The account the row is about.</param>
    private static async Task AddAccountEntryAsync(WebApplicationFactory<Program> factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), null, "olive", "SiteCreated", "acme.example.com",
            "192.0.2.1", "ua", true, null, null, accountId));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Gives a login a TOTP secret and returns the three secret values the login now has at rest: its
    /// password hash, that secret, and the digest of its newest refresh token.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login.</param>
    private static async Task<string[]> PlantSecretsAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Username == username);
        user.EnableTotp(PlantedTotpSecret);
        await context.SaveChangesAsync();
        var tokenHash = await context.Sessions
            .Where(session => session.UserId == user.Id)
            .Select(session => session.TokenHash)
            .FirstAsync();

        return [user.PasswordHash, PlantedTotpSecret, tokenHash];
    }

    /// <summary>Reads the journal rows recording a personal-data export.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task<List<AuditEvent>> ExportEntriesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.AuditEvents
            .Where(entry => entry.Action == IdentityAuditActions.PersonalDataExported)
            .ToListAsync();
    }

    /// <summary>Starts viewing as an account and returns a client carrying the view-as token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="accountId">The account to view as.</param>
    private static async Task<HttpClient> StartViewAsAsync(
        WebApplicationFactory<Program> factory,
        HttpClient admin,
        Guid accountId)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/view-as", new { AccountId = accountId });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString()!);

        return client;
    }
}
