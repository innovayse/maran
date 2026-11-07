using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.Configuration;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The audit export (<c>GET /api/v1/audit/export</c>) over real HTTP against a real PostgreSQL: the CSV
/// and JSON files, every filter the list takes, the download headers, the cap, the refusal of every
/// caller that is not an administrator, the rate limit, and the journal entry the export leaves behind.
/// </summary>
/// <remarks>
/// Threat note: docs/superpowers/notes/2026-11-07-audit-retention-erasure-threat-note.md (second review
/// OUTSTANDING). Entries are seeded under the action <c>ExportSeed</c> (or <c>BulkSeed</c> for the cap) and
/// requests name it as a filter, because signing in writes entries of its own and a count over the whole
/// journal would move with them.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AuditExportEndpointTests : IAsyncLifetime
{
    /// <summary>The instant the seeded entries count from, well before any sign-in the test makes.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The route under test.</summary>
    private const string Route = "/api/v1/audit/export";

    /// <summary>The action the seeded entries record.</summary>
    private const string SeedAction = "ExportSeed";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuditExportEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// A CSV export is an attachment named for the clock, uncacheable, with the header record and the
    /// filtered rows oldest first.
    /// </summary>
    [Fact]
    public async Task A_csv_export_is_an_uncacheable_attachment_with_the_header_and_the_rows_oldest_first()
    {
        await using var factory = CreateFactory();
        var older = Entry("olive", "older.example.com", true, Epoch);
        var newer = Entry("pepper", "newer.example.com", false, Epoch.AddDays(1));
        await SeedWorldAsync(factory, newer, older);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"{Route}?format=csv&action={SeedAction}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Matches(
            new Regex("^attachment; filename=\"maran-audit-\\d{8}T\\d{6}Z\\.csv\"$"),
            response.Content.Headers.GetValues("Content-Disposition").Single());

        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n");
        Assert.StartsWith("id,occurredAt,actorUserId,actorUsername,", lines[0], StringComparison.Ordinal);
        Assert.Contains($"{older.Id},2026-01-01T00:00:00.000000Z,,olive,,{SeedAction},succeeded,older.example.com,", lines[1], StringComparison.Ordinal);
        Assert.Contains($"{newer.Id},2026-01-02T00:00:00.000000Z,,pepper,,{SeedAction},failed,newer.example.com,", lines[2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(4, lines.Length);
    }

    /// <summary>With no format named the export is JSON: an array of objects carrying the fourteen columns.</summary>
    [Fact]
    public async Task With_no_format_named_the_export_is_a_json_array_of_the_fourteen_columns()
    {
        await using var factory = CreateFactory();
        var entry = Entry("olive", "shop.example.com", true, Epoch);
        await SeedWorldAsync(factory, entry);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"{Route}?action={SeedAction}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(
            new Regex("^attachment; filename=\"maran-audit-\\d{8}T\\d{6}Z\\.json\"$"),
            response.Content.Headers.GetValues("Content-Disposition").Single());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(14, row.EnumerateObject().Count());
        Assert.Equal(entry.Id, row.GetProperty("id").GetGuid());
        Assert.Equal("olive", row.GetProperty("actorUsername").GetString());
        Assert.Equal("succeeded", row.GetProperty("outcome").GetString());
        Assert.Equal("shop.example.com", row.GetProperty("subject").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("rowHash").ValueKind);
    }

    /// <summary>
    /// Each filter the list takes narrows the export to its own rows, on PostgreSQL: the outcome, the
    /// actor without regard to case, the window, the account and the search, which is an <c>ILIKE</c>.
    /// </summary>
    [Fact]
    public async Task Each_filter_narrows_the_export_to_its_own_rows()
    {
        await using var factory = CreateFactory();
        var account = Guid.NewGuid();
        var early = Entry("olive_ops", "early.example.com", true, Epoch);
        var refused = Entry("pepper", "refused.example.com", false, Epoch.AddDays(2));
        var late = Entry("olive_ops", "late.example.com", true, Epoch.AddDays(5), account);
        await SeedWorldAsync(factory, early, refused, late);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var byOutcome = await SubjectsAsync(admin, $"action={SeedAction}&outcome=failed");
        var byActor = await SubjectsAsync(admin, $"action={SeedAction}&actor=OLIVE_OPS");
        var byWindow = await SubjectsAsync(
            admin,
            $"action={SeedAction}&from={Uri.EscapeDataString(Epoch.AddDays(2).ToString("O"))}"
            + $"&to={Uri.EscapeDataString(Epoch.AddDays(5).ToString("O"))}");
        var byAccount = await SubjectsAsync(admin, $"action={SeedAction}&account={account}");
        var bySearch = await SubjectsAsync(admin, $"action={SeedAction}&search=refused.example");
        var byWildcard = await SubjectsAsync(admin, $"action={SeedAction}&actor=olive_ops&search={Uri.EscapeDataString("%")}");

        Assert.Equal(["refused.example.com"], byOutcome);
        Assert.Equal(["early.example.com", "late.example.com"], byActor);
        Assert.Equal(["refused.example.com", "late.example.com"], byWindow);
        Assert.Equal(["late.example.com"], byAccount);
        Assert.Equal(["refused.example.com"], bySearch);
        Assert.Empty(byWildcard);
    }

    /// <summary>
    /// A subject and a user agent that an attacker typed, beginning with a formula character, reach the
    /// CSV with a leading apostrophe; the JSON file carries them verbatim.
    /// </summary>
    [Fact]
    public async Task A_hostile_subject_and_user_agent_are_neutralised_in_the_csv_and_verbatim_in_the_json()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(
            factory,
            new AuditEvent(
                Guid.NewGuid(), Epoch, null, "mallory", SeedAction, "=HYPERLINK(\"http://x\")", "203.0.113.7", "@SUM(1+1)", false, null));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var csv = await (await admin.GetAsync($"{Route}?format=csv&action={SeedAction}")).Content.ReadAsStringAsync();
        var json = await (await admin.GetAsync($"{Route}?format=json&action={SeedAction}")).Content.ReadAsStringAsync();

        Assert.Contains(",\"'=HYPERLINK(\"\"http://x\"\")\",203.0.113.7,'@SUM(1+1),", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",@SUM", csv, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(json);
        var row = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("=HYPERLINK(\"http://x\")", row.GetProperty("subject").GetString());
        Assert.Equal("@SUM(1+1)", row.GetProperty("userAgent").GetString());
    }

    /// <summary>
    /// The export is journalled as the administrator, with their user agent, the format, the filters and
    /// the row count.
    /// </summary>
    [Fact]
    public async Task An_export_is_journalled_with_the_administrator_the_format_the_filters_and_the_row_count()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(
            factory,
            Entry("olive", "one.example.com", true, Epoch),
            Entry("olive", "two.example.com", true, Epoch.AddDays(1)));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        admin.DefaultRequestHeaders.UserAgent.ParseAdd("export-test/1.0");

        var response = await admin.GetAsync($"{Route}?format=csv&action={SeedAction}&outcome=succeeded");
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n");

        Assert.Equal(4, lines.Length);
        var entry = Assert.Single(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));
        Assert.Equal("admin", entry.ActorUsername);
        Assert.True(entry.Succeeded);
        Assert.Equal($"format=csv rows=2 action={SeedAction} outcome=succeeded", entry.Subject);
        Assert.Equal("export-test/1.0", entry.UserAgent);
        Assert.False(string.IsNullOrEmpty(entry.IpAddress));
    }

    /// <summary>
    /// The entry an export writes before it streams matches the filter of its own export (the actor is
    /// the administrator asking), and the file still does not contain it: the writer stops at the rows
    /// that were counted, which the entry's subject states.
    /// </summary>
    [Fact]
    public async Task The_entry_an_export_writes_matches_its_own_filter_yet_is_not_in_its_file()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"{Route}?format=json&actor=admin");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var written = body.RootElement.EnumerateArray().ToList();
        var entry = Assert.Single(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal($"format=json rows={written.Count} actor=admin", entry.Subject);
        Assert.NotEmpty(written);
        Assert.DoesNotContain(written, row =>
        {
            return row.GetProperty("action").GetString() == IdentityAuditActions.AuditExported;
        });
    }

    /// <summary>
    /// More rows than the cap is refused with 400 <c>AuditExportTooLarge</c> and no entry; exactly the cap
    /// is accepted and journalled. Half a million rows are inserted by one statement.
    /// </summary>
    [Fact]
    public async Task A_window_above_the_cap_is_refused_and_exactly_the_cap_is_accepted()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        await InsertBulkAsync(factory, AuditExportLimits.MaxRows + 1);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var tooLarge = await admin.GetAsync($"{Route}?format=csv&action=BulkSeed");

        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Equal("AuditExportTooLarge", await ProblemDocument.CodeAsync(tooLarge));
        Assert.Empty(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));

        await DeleteOneBulkAsync(factory);

        // Only the headers are read: the file is half a million rows, and the claim here is that the
        // export is accepted and recorded, which is true before the first row is streamed.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Route}?format=csv&action=BulkSeed");
        using var accepted = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var entry = Assert.Single(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));
        Assert.Equal($"format=csv rows={AuditExportLimits.MaxRows} action=BulkSeed", entry.Subject);
    }

    /// <summary>A bound the export refuses answers 400 with its code, and writes no entry.</summary>
    /// <param name="queryString">The refused query string.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("format=xml", "RequestParameterInvalid")]
    [InlineData("format=csv&outcome=sideways", "RequestParameterInvalid")]
    [InlineData("action=Site%25", "AuditActionFilterInvalid")]
    [InlineData("actor=olive%20ops", "AuditActorFilterInvalid")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "AuditDateRangeInverted")]
    public async Task A_bound_the_export_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync($"{Route}?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemDocument.CodeAsync(response));
        Assert.Empty(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));
    }

    /// <summary>
    /// A customer, a reseller, an administrator viewing as a customer and an anonymous caller are all
    /// refused, and none of them leaves an export entry; the administrator beside them is the inverse
    /// control, so a refusal that came from a broken route would not pass.
    /// </summary>
    [Fact]
    public async Task Every_caller_that_is_not_an_administrator_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        await IdentityResellerWorld.SavePolicyAsync(factory, forceTwoFactorForAdmins: false, forceTwoFactorForResellers: false);
        await IdentityResellerWorld.SeedResellerAsync(factory, "nova");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        using var reseller = await EndpointHarness.SignInAsync(factory, "nova");
        using var viewing = await IdentityResellerWorld.StartViewingAccountAsync(factory, admin, accountId);
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Route}?action={SeedAction}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reseller.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewing.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        Assert.Single(await ReadAuditAsync(factory, IdentityAuditActions.AuditExported));
    }

    /// <summary>An API key is refused whatever scopes it holds: it reaches no administrator surface.</summary>
    [Fact]
    public async Task An_api_key_is_refused()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await ApiKeyHost.CreateKeyAsync(admin, "export attempt");

        var response = await host.GetAsync(Route, issued.Secret);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"answered {(int)response.StatusCode}");
        Assert.Empty(await host.ReadAuditAsync(IdentityAuditActions.AuditExported));
    }

    /// <summary>
    /// At the shipped settings (6 exports an hour per administrator) the seventh is refused with 429, the
    /// panel's problem shape and a Retry-After, and the refused request leaves no entry.
    /// </summary>
    /// <remarks>
    /// The host is booted with no rate-limit override, so the numbers under test are the ones the options
    /// ship. The first assertion is the precondition: the environment outranks appsettings, and a shell
    /// that sourced <c>scripts/dev</c> carries the widened development value into this host, which would
    /// then answer seven 200s over a limiter that works.
    /// </remarks>
    [Fact]
    public async Task The_seventh_export_in_an_hour_is_refused_at_the_shipped_settings()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory);
        var limits = factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        Assert.True(
            limits is { AuditExportMaxRequests: 6, AuditExportWindowSeconds: 3600 },
            "The host is not at the shipped export limits; an environment variable such as "
            + "RateLimiting__AuditExportMaxRequests overrides them. Run the suite through "
            + $"`maran test backend`. Bound: {limits.AuditExportMaxRequests}/{limits.AuditExportWindowSeconds}.");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? seventh = null;
        for (var attempt = 1; attempt <= 7; attempt++)
        {
            seventh = await admin.GetAsync($"{Route}?action={SeedAction}");
            statuses.Add(seventh.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, 6), statuses.Take(6));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[6]);
        Assert.Equal("application/problem+json", seventh!.Content.Headers.ContentType?.MediaType);
        Assert.True(seventh.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("HostRateLimited", await ProblemDocument.CodeAsync(seventh));
        Assert.Equal(6, (await ReadAuditAsync(factory, IdentityAuditActions.AuditExported)).Count);
    }

    /// <summary>
    /// Boots a host whose export budget is wide, so a test that exports several times is not the rate
    /// limit's test; the shipped number is held by its own test.
    /// </summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.PostConfigure<RateLimitOptions>(options =>
            {
                options.AuditExportMaxRequests = 1_000;
                options.AuditExportWindowSeconds = 3600;
            });
        });
    }

    /// <summary>Migrates, seeds the administrator, and stores the given journal entries.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="entries">The entries to store.</param>
    private static async Task SeedWorldAsync(WebApplicationFactory<Program> factory, params AuditEvent[] entries)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.AuditEvents.AddRange(entries);
        await identity.SaveChangesAsync();
    }

    /// <summary>Stores <paramref name="count"/> entries recording <c>BulkSeed</c> with one statement.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="count">How many entries.</param>
    private static async Task InsertBulkAsync(WebApplicationFactory<Program> factory, int count)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.ExecuteSqlInterpolatedAsync($"""
            insert into identity."AuditEvents"
                ("Id", "OccurredAt", "ActorUsername", "Action", "Subject", "IpAddress", "UserAgent", "Succeeded")
            select gen_random_uuid(), timestamptz '2026-01-01 00:00:00+00' + (n * interval '1 millisecond'),
                   'bulk', 'BulkSeed', 'row ' || n, '203.0.113.7', 'seed', true
            from generate_series(1, {count}) as n
            """);
    }

    /// <summary>Deletes one of the <c>BulkSeed</c> entries, which brings an over-the-cap window to exactly the cap.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task DeleteOneBulkAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.ExecuteSqlInterpolatedAsync($"""
            delete from identity."AuditEvents"
            where "Id" = (select "Id" from identity."AuditEvents" where "Action" = 'BulkSeed' limit 1)
            """);
    }

    /// <summary>Reads every journal entry of one action, oldest first, straight from the database.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="action">The action name.</param>
    /// <returns>The entries.</returns>
    private static async Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(
        WebApplicationFactory<Program> factory,
        string action)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == action)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Exports as JSON with the given filters and answers the subjects of the rows, in file order.</summary>
    /// <param name="client">The signed-in administrator.</param>
    /// <param name="filters">The query string, without the format.</param>
    /// <returns>The subjects.</returns>
    private static async Task<List<string>> SubjectsAsync(HttpClient client, string filters)
    {
        var response = await client.GetAsync($"{Route}?format=json&{filters}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray()
            .Select(row => { return row.GetProperty("subject").GetString()!; })
            .ToList();
    }

    /// <summary>Builds one journal entry recording <see cref="SeedAction"/>.</summary>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="accountId">The account the entry is about, or null.</param>
    private static AuditEvent Entry(
        string actor,
        string subject,
        bool succeeded,
        DateTimeOffset at,
        Guid? accountId = null)
    {
        return new AuditEvent(
            Guid.NewGuid(), at, null, actor, SeedAction, subject, "203.0.113.7", "test", succeeded, null, null, accountId);
    }
}
