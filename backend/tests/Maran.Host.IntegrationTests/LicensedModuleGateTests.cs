using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The request-time licence gate on the wire: a controller of a paid module, reached through the real
/// pipeline, is refused until an installed licence covers it, and refused again when that cover ends,
/// while the core of the panel is served in every one of those states.
/// </summary>
/// <remarks>
/// <para>
/// The paid module is <see cref="LicensedProbeController"/>, because no module the panel ships is paid.
/// The licence is installed through the real install endpoint, signed with a key that exists only in this
/// project (<see cref="SignedTestLicence"/>) and trusted by the host through the container; time is a
/// clock the test moves, so expiry is passed without sleeping. Every refusal is paired with the request
/// that must be served, so a gate mutated to refuse everything fails the second half.
/// </para>
/// <para>
/// UNOBSERVED HERE: the real lease file written by a renewal (nothing writes one yet, issue #186), the
/// production verification key (a release embeds it), and the 30-second memory of the window, which the
/// clock step passes over rather than measures.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class LicensedModuleGateTests : IAsyncLifetime
{
    /// <summary>The probe's address.</summary>
    private const string ProbePath = "/" + LicensedProbeController.RoutePath;

    /// <summary>The licence status endpoint, which no gate stands in front of.</summary>
    private const string StatusPath = "/api/v1/licence-status";

    /// <summary>The install endpoint.</summary>
    private const string InstallPath = "/api/v1/licence-status/install";

    /// <summary>How long a test licence lives; shorter than the 15-minute access token the test signs in with.</summary>
    private static readonly TimeSpan LicenceLife = TimeSpan.FromMinutes(10);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>A directory of its own for the licence and lease files, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "maran-gate-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public LicensedModuleGateTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Removes the files the test wrote.</summary>
    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);

        return Task.CompletedTask;
    }

    /// <summary>The paid probe is refused with no licence, served under one that covers it, refused again once it expires, and the core is served throughout.</summary>
    [Fact]
    public async Task A_paid_module_is_refused_without_a_licence_served_with_one_and_refused_again_after_it_expires()
    {
        var clock = new AdvancingClock();
        await using var factory = await BootAsync(clock);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await AssertRefusedAsync(admin);
        await AssertCoreServedAsync(admin, factory, expectedState: "absent");

        await InstallAsync(admin, SignedTestLicence.Naming(LicensedProbeController.ModuleId, clock.UtcNow + LicenceLife, offline: true));

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ProbePath)).StatusCode);
        await AssertCoreServedAsync(admin, factory, expectedState: "valid");

        clock.Advance(LicenceLife + TimeSpan.FromMinutes(1));

        await AssertRefusedAsync(admin);
        await AssertCoreServedAsync(admin, factory, expectedState: "refused");
    }

    /// <summary>A licence that covers another module leaves the probe refused; the licence that names it, installed after, serves it.</summary>
    [Fact]
    public async Task A_licence_that_names_another_module_does_not_cover_the_paid_module()
    {
        var clock = new AdvancingClock();
        await using var factory = await BootAsync(clock);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await InstallAsync(admin, SignedTestLicence.Naming("some-other-module", clock.UtcNow + LicenceLife, offline: true));
        await AssertRefusedAsync(admin);
        await AssertCoreServedAsync(admin, factory, expectedState: "valid");

        await InstallAsync(admin, SignedTestLicence.Naming(LicensedProbeController.ModuleId, clock.UtcNow + LicenceLife, offline: true));

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ProbePath)).StatusCode);
    }

    /// <summary>An online licence with no lease lapses its module at once: refused on the wire, with the licence itself reading valid.</summary>
    [Fact]
    public async Task An_online_licence_with_no_lease_refuses_the_paid_module_and_serves_the_core()
    {
        var clock = new AdvancingClock();
        await using var factory = await BootAsync(clock);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await InstallAsync(admin, SignedTestLicence.Naming(LicensedProbeController.ModuleId, clock.UtcNow + LicenceLife, offline: false));

        await AssertRefusedAsync(admin);
        await AssertCoreServedAsync(admin, factory, expectedState: "valid");
    }

    /// <summary>A lapse and its recovery are written to the audit journal once each, as the panel, with no account on the entry.</summary>
    [Fact]
    public async Task A_lapse_and_its_recovery_are_written_to_the_audit_journal_as_the_panel()
    {
        var clock = new AdvancingClock();
        await using var factory = await BootAsync(clock);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var watcher = factory.Services.GetRequiredService<LicenceCoverageWatcher>();

        await InstallAsync(admin, SignedTestLicence.Naming(LicensedProbeController.ModuleId, clock.UtcNow + LicenceLife, offline: true));
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Empty(await TransitionsAsync(factory));

        clock.Advance(LicenceLife + TimeSpan.FromMinutes(1));
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        var lapse = Assert.Single(await TransitionsAsync(factory));
        Assert.Equal(LicenceAuditActions.ModuleLapsed, lapse.Action);
        Assert.Equal($"module={LicensedProbeController.ModuleId};from=Covered;to=Lapsed", lapse.Subject);
        Assert.Equal(SystemAuditEntry.NameFor("licensing"), lapse.ActorUsername);
        Assert.Null(lapse.AccountId);

        // One second on, so the two entries are ordered by the instant they were written and not by a tie.
        clock.Advance(TimeSpan.FromSeconds(1));
        await InstallAsync(admin, SignedTestLicence.Naming(LicensedProbeController.ModuleId, clock.UtcNow + LicenceLife, offline: true));
        await watcher.ObserveAsync(CancellationToken.None);

        var entries = await TransitionsAsync(factory);
        Assert.Equal(
            [LicenceAuditActions.ModuleLapsed, LicenceAuditActions.ModuleRecovered],
            entries.Select(entry => { return entry.Action; }));
        Assert.Equal($"module={LicensedProbeController.ModuleId};from=Lapsed;to=Covered", entries[1].Subject);
    }

    /// <summary>Boots a host that maps the paid probe, trusts the test key, reads time from <paramref name="clock"/> and keeps its licence in a directory of its own.</summary>
    /// <param name="clock">The clock the test moves.</param>
    /// <returns>The booted, migrated host with an administrator, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync(AdvancingClock clock)
    {
        var factory = EndpointHarness.CreateFactory(
            _pg,
            services =>
            {
                services.AddControllers().AddApplicationPart(typeof(LicensedProbeController).Assembly);
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<LicenceVerificationKey>();
                services.AddSingleton(SignedTestLicence.TrustedKey);
            },
            new Dictionary<string, string>
            {
                // "No licence is installed" is a fact this test creates: a path in a fresh directory cannot exist.
                ["Licensing:LicenceFilePath"] = Path.Combine(_directory, "licence.json"),
                ["Licensing:LeaseFilePath"] = Path.Combine(_directory, "lease.json"),
            });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        return factory;
    }

    /// <summary>Installs a licence through the real endpoint and requires it to be accepted.</summary>
    /// <param name="admin">A signed-in administrator.</param>
    /// <param name="licenceText">The signed envelope.</param>
    private static async Task InstallAsync(HttpClient admin, string licenceText)
    {
        var response = await admin.PostAsJsonAsync(InstallPath, new { RawLicenceText = licenceText });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The probe answers 403 with the licence code, and says nothing else about the licence.</summary>
    /// <param name="admin">A signed-in administrator: the licence binds the server, so the role does not matter.</param>
    private static async Task AssertRefusedAsync(HttpClient admin)
    {
        var response = await admin.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ModuleLicenceRequired", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>The health endpoint, the licence status, and the sign-in are served, with the status naming the expected state.</summary>
    /// <param name="admin">A signed-in administrator.</param>
    /// <param name="factory">The booted host, to reach the anonymous endpoints of.</param>
    /// <param name="expectedState">The licence state the status endpoint must report.</param>
    private static async Task AssertCoreServedAsync(
        HttpClient admin,
        WebApplicationFactory<Program> factory,
        string expectedState)
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);

        var status = await admin.GetAsync(StatusPath);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal(expectedState, body.RootElement.GetProperty("state").GetString());

        // Signing in is asked for its answer and not its token: a token minted at the test's moved clock
        // would be refused by the bearer handler, which reads the real one.
        var signIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = "admin", Password = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    /// <summary>Reads the lapse and recovery entries the journal holds, oldest first.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The entries.</returns>
    private static async Task<List<AuditEvent>> TransitionsAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await identity.AuditEvents
            .Where(entry => entry.Action == LicenceAuditActions.ModuleLapsed
                || entry.Action == LicenceAuditActions.ModuleRecovered)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }
}
