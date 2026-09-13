using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The chart's bucketing SQL against the PostgreSQL that actually runs it: <c>date_bin</c> on read
/// (R10), and R7's rate arithmetic over a sampler gap and a counter reset.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot be a unit test.</b> The query is raw SQL built on <c>date_bin</c> and on
/// PostgreSQL's ordered-aggregate form for "the last reading in this bucket". Neither has a LINQ
/// translation and neither is implemented by the in-memory provider, so a unit test could only
/// exercise a query that cannot run — the shape of test that agrees with any implementation. Driven
/// here, the statement is the one production issues.
/// </para>
/// <para>
/// <b>There is no rollup table (R10).</b> Everything below is computed from the raw sample rows at
/// read time, which is why the seeded rows and the drawn points can never disagree.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MonitoringChartTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The five-minute bucket the day range uses, as a span, for aligning seeded samples.</summary>
    private static readonly TimeSpan DayBucket = TimeSpan.FromMinutes(5);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MonitoringChartTests(PostgresFixture postgres)
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

    /// <summary>A sampler gap and a counter reset come back as a measured rate and a clamped zero.</summary>
    /// <remarks>
    /// The two hazards R7 names, in one series, through the real SQL. The gap is ten minutes where the
    /// nominal cadence is one, so a divisor of "the sampling interval" would report ten times the
    /// traffic; the reset makes the newer counter smaller than the older one, which without the clamp
    /// is a downward spike of billions of bytes per second on exactly the day somebody is looking.
    /// </remarks>
    [Fact]
    public async Task A_sampler_gap_and_a_counter_reset_come_back_as_a_measured_rate_and_a_clamped_zero()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);

        var origin = AlignedInstant(factory, TimeSpan.FromMinutes(60));
        await SeedSamplesAsync(factory,
        [
            Sample(origin, receivedBytes: 1_000_000, sentBytes: 2_000_000, cpuPercent: 10),
            Sample(origin.AddMinutes(10), receivedBytes: 1_600_000, sentBytes: 2_600_000, cpuPercent: 20),
            Sample(origin.AddMinutes(20), receivedBytes: 500, sentBytes: 400, cpuPercent: 30),
        ]);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/monitoring/chart?range=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var buckets = body.RootElement.GetProperty("buckets").EnumerateArray().ToList();

        Assert.Equal(3, buckets.Count);
        Assert.Equal(300, body.RootElement.GetProperty("bucketSeconds").GetInt32());

        // The first bucket has no earlier reading to measure against, so it has no rate at all —
        // never a zero, which would draw as a minute of genuine silence.
        Assert.Equal(JsonValueKind.Null, buckets[0].GetProperty("networkReceivedBytesPerSecond").ValueKind);

        // 600,000 bytes across the 600 seconds that ACTUALLY elapsed.
        Assert.Equal(1_000d, buckets[1].GetProperty("networkReceivedBytesPerSecond").GetDouble(), precision: 3);
        Assert.Equal(1_000d, buckets[1].GetProperty("networkSentBytesPerSecond").GetDouble(), precision: 3);

        // The reboot: the counter went backwards, and that is no traffic rather than negative traffic.
        Assert.Equal(0d, buckets[2].GetProperty("networkReceivedBytesPerSecond").GetDouble());
        Assert.Equal(0d, buckets[2].GetProperty("networkSentBytesPerSecond").GetDouble());
    }

    /// <summary>Samples inside one bucket are averaged by PostgreSQL, not returned one by one.</summary>
    /// <remarks>
    /// This is the <c>date_bin</c> grouping itself: two samples two minutes apart fall in one
    /// five-minute bucket, and the level metrics come back as their mean. Without the grouping a
    /// seven-day chart would return ten thousand points.
    /// </remarks>
    [Fact]
    public async Task Samples_inside_one_bucket_are_averaged_into_a_single_point()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);

        var origin = AlignedInstant(factory, TimeSpan.FromMinutes(60));
        await SeedSamplesAsync(factory,
        [
            Sample(origin.AddSeconds(30), receivedBytes: 10, sentBytes: 10, cpuPercent: 20),
            Sample(origin.AddSeconds(90), receivedBytes: 20, sentBytes: 20, cpuPercent: 40),
        ]);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/monitoring/chart?range=1");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bucket = Assert.Single(body.RootElement.GetProperty("buckets").EnumerateArray().ToList());

        Assert.Equal(30d, bucket.GetProperty("cpuPercent").GetDouble(), precision: 6);
    }

    /// <summary>The week range buckets the same rows more coarsely, on the same rows.</summary>
    /// <remarks>
    /// Proof that the bucketing really is on READ: the two ranges answer from one table, with no
    /// second write path and nothing that could make a summary disagree with its samples.
    /// </remarks>
    [Fact]
    public async Task The_week_range_buckets_the_same_rows_more_coarsely()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);

        var origin = AlignedInstant(factory, TimeSpan.FromMinutes(60));
        await SeedSamplesAsync(factory,
        [
            Sample(origin, receivedBytes: 10, sentBytes: 10, cpuPercent: 10),
            Sample(origin.AddMinutes(10), receivedBytes: 20, sentBytes: 20, cpuPercent: 20),
            Sample(origin.AddMinutes(20), receivedBytes: 30, sentBytes: 30, cpuPercent: 30),
        ]);

        using var client = await SignInAsync(factory, "admin");

        using var day = JsonDocument.Parse(
            await (await client.GetAsync("/api/v1/monitoring/chart?range=1")).Content.ReadAsStringAsync());
        using var week = JsonDocument.Parse(
            await (await client.GetAsync("/api/v1/monitoring/chart?range=2")).Content.ReadAsStringAsync());

        Assert.Equal(3, day.RootElement.GetProperty("buckets").GetArrayLength());
        Assert.Equal(300, day.RootElement.GetProperty("bucketSeconds").GetInt32());

        // Thirty-minute buckets swallow all three samples spread over twenty minutes.
        Assert.Equal(1_800, week.RootElement.GetProperty("bucketSeconds").GetInt32());
        Assert.True(week.RootElement.GetProperty("buckets").GetArrayLength() <= 2);
    }

    /// <summary>A panel with no samples yet draws an empty chart rather than failing.</summary>
    [Fact]
    public async Task A_panel_with_no_samples_yet_answers_an_empty_chart()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/monitoring/chart?range=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("buckets").GetArrayLength());
    }

    /// <summary>A range the panel does not offer is refused rather than silently answered with a day.</summary>
    [Fact]
    public async Task A_range_the_panel_does_not_offer_is_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/monitoring/chart?range=99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Builds one sample row at a given instant.</summary>
    /// <param name="capturedAt">When the reading was taken.</param>
    /// <param name="receivedBytes">The received-bytes counter.</param>
    /// <param name="sentBytes">The sent-bytes counter.</param>
    /// <param name="cpuPercent">Processor utilisation at the reading.</param>
    /// <returns>The sample.</returns>
    private static MetricSample Sample(
        DateTimeOffset capturedAt,
        long receivedBytes,
        long sentBytes,
        double cpuPercent)
    {
        return new MetricSample(
            capturedAt, cpuPercent, 1_000, 4_000, 500, 1_000, receivedBytes, sentBytes, 0.5);
    }

    /// <summary>An instant in the past that sits exactly on a five-minute bucket boundary.</summary>
    /// <param name="factory">The booted host, for its clock.</param>
    /// <param name="ago">How far back to start.</param>
    /// <returns>The aligned instant.</returns>
    /// <remarks>
    /// Aligned deliberately. Two samples ten minutes apart always land in different five-minute
    /// buckets whatever the alignment, but two samples ONE minute apart land in the same bucket only
    /// when they do not straddle a boundary — and a test that passed or failed depending on the
    /// minute the suite ran at would be exactly the flake rules/testing.md calls a P1.
    /// </remarks>
    private static DateTimeOffset AlignedInstant(WebApplicationFactory<Program> factory, TimeSpan ago)
    {
        using var scope = factory.Services.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow - ago;

        var ticksPerBucket = DayBucket.Ticks;
        return new DateTimeOffset(now.UtcTicks - (now.UtcTicks % ticksPerBucket), TimeSpan.Zero);
    }

    /// <summary>Writes sample rows straight into the module's own table.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="samples">The rows to write.</param>
    private static async Task SeedSamplesAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<MetricSample> samples)
    {
        using var scope = factory.Services.CreateScope();
        var monitoring = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        monitoring.Samples.AddRange(samples);
        await monitoring.SaveChangesAsync();
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

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(new StubAgentMonitorClient());
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
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
