using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Tests.Services;

/// <summary>
/// What the panel does at start-up to make the name server match the database: a zone whose served serial
/// differs from its row is published again, a served zone with no row is removed, nothing is touched while DNS
/// is not in service, and one zone's failure does not stop the rest. The pass runs with no principal, so the
/// tenant filter shows it no row; each test that depends on seeing past it says so.
/// </summary>
public sealed class DnsZoneReconcilerTests
{
    /// <summary>The account the zones belong to.</summary>
    private static readonly Guid AccountId = new("44444444-4444-4444-8444-444444444444");

    /// <summary>Builds an administrator's world over a database holding the named zones, the server on.</summary>
    /// <param name="zoneNames">The apex names to create, in order.</param>
    /// <returns>The world; its own agent has served nothing, and the database holds a row per name.</returns>
    private static async Task<DnsTestContext> WorldAsync(params string[] zoneNames)
    {
        var admin = new DnsTestContext();
        admin.Accounts.Add(AccountId, "dana", maxDnsZones: 10);
        await admin.EnableDnsServerAsync();
        foreach (var name in zoneNames)
        {
            var created = await admin.CreateZone.HandleAsync(
                new CreateZoneCommand(AccountId, name, "203.0.113.1", "tests"), CancellationToken.None);
            Assert.True(created.IsSuccess);
        }

        return admin;
    }

    /// <summary>Makes the start-up caller's stand-in agent serve a zone at a serial, then forgets the setup call.</summary>
    /// <param name="agent">The agent whose daemon serves the zone.</param>
    /// <param name="name">The apex.</param>
    /// <param name="serial">The served serial.</param>
    private static async Task ServeAsync(RecordingDnsAgent agent, string name, long serial)
    {
        var definition = new DnsZoneDefinition(name, serial, "ns1.panel.test", "hostmaster.panel.test", ["ns1.panel.test"], []);
        await agent.PutZoneAsync(definition, CancellationToken.None);
        agent.Puts.Clear();
    }

    /// <summary>Builds the start-up caller's view of the same database: an anonymous principal and its own agent.</summary>
    /// <param name="admin">The world that seeded the rows.</param>
    /// <returns>The start-up world.</returns>
    private static DnsTestContext StartUp(DnsTestContext admin)
    {
        return new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
    }

    /// <summary>Reads the stored serial of every zone, through a fresh administrator context.</summary>
    /// <param name="admin">The world whose database is read.</param>
    /// <returns>Serial by apex name.</returns>
    private static async Task<Dictionary<string, long>> SerialsAsync(DnsTestContext admin)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), admin.DatabaseName);

        return await reader.Database.Zones.ToDictionaryAsync(zone =>
        {
            return zone.Name;
        }, zone =>
        {
            return zone.Serial;
        });
    }

    /// <summary>Builds the reconciler over a world's context and agent.</summary>
    /// <param name="world">The start-up world.</param>
    /// <param name="agent">The agent it talks to.</param>
    /// <param name="logger">Where it reports.</param>
    /// <returns>The reconciler and the container that must outlive it.</returns>
    private static (DnsZoneReconciler Reconciler, DnsReconcilerScopeFactory Scopes) Build(
        DnsTestContext world,
        IAgentDnsClient agent,
        RecordingLogger<DnsZoneReconciler>? logger = null)
    {
        var scopes = new DnsReconcilerScopeFactory(world.Database, agent);

        return (new DnsZoneReconciler(scopes.Scopes, logger ?? new RecordingLogger<DnsZoneReconciler>()), scopes);
    }

    /// <summary>A zone served at the serial of its row is left alone: nothing is published or removed.</summary>
    [Fact]
    public async Task A_zone_in_step_with_its_row_is_left_alone()
    {
        using var admin = await WorldAsync("example.com");
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "example.com", (await SerialsAsync(admin))["example.com"]);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.True(settled);
        Assert.Empty(startup.Agent.Puts);
        Assert.Empty(startup.Agent.Deletes);
    }

    /// <summary>A zone the daemon serves ahead of its row is published from the row under a serial past the served one.</summary>
    [Fact]
    public async Task A_zone_served_ahead_of_its_row_is_republished_past_the_served_serial()
    {
        using var admin = await WorldAsync("example.com");
        var added = await admin.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("www", RecordType.A, 300, "203.0.113.7"), (await admin.Database.Zones.SingleAsync()).Id),
            CancellationToken.None);
        Assert.True(added.IsSuccess);
        var rowSerial = (await SerialsAsync(admin))["example.com"];
        using var startup = StartUp(admin);
        var ahead = rowSerial + 50;
        await ServeAsync(startup.Agent, "example.com", ahead);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.True(settled);
        var sent = Assert.Single(startup.Agent.Puts);
        Assert.True(sent.Serial > ahead);
        Assert.Equal("www", Assert.Single(sent.Records).Name);
        Assert.Equal(sent.Serial, (await SerialsAsync(admin))["example.com"]);
        Assert.Equal(sent.Serial, (await startup.Agent.GetZoneAsync("example.com", CancellationToken.None)).Value.Serial);
    }

    /// <summary>A zone the daemon serves behind its row is published from the row under the row's own serial.</summary>
    [Fact]
    public async Task A_zone_served_behind_its_row_is_republished_under_the_rows_serial()
    {
        using var admin = await WorldAsync("example.com");
        var rowSerial = (await SerialsAsync(admin))["example.com"];
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "example.com", rowSerial - 1);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(rowSerial, Assert.Single(startup.Agent.Puts).Serial);
        Assert.Equal(rowSerial, (await SerialsAsync(admin))["example.com"]);
    }

    /// <summary>A zone with a row that the daemon does not serve at all is published from the row.</summary>
    [Fact]
    public async Task A_zone_the_host_lost_is_published_again_from_its_row()
    {
        using var admin = await WorldAsync("example.com", "example.org");
        var rows = await SerialsAsync(admin);
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "example.org", rows["example.org"]);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        await reconciler.ReconcileAsync(CancellationToken.None);

        var sent = Assert.Single(startup.Agent.Puts);
        Assert.Equal("example.com", sent.Name);
        Assert.Equal(rows["example.com"], sent.Serial);
    }

    /// <summary>The pass sees every tenant's zones although the tenant filter shows its own caller none.</summary>
    [Fact]
    public async Task The_pass_sees_past_the_tenant_filter()
    {
        // The control: the same database read as the start-up caller finds no zone. Without the filter
        // bypass the pass would find no row and remove every served zone as unowned.
        using var admin = await WorldAsync("example.com");
        using var startup = StartUp(admin);
        Assert.Empty(startup.Database.Zones);
        await ServeAsync(startup.Agent, "example.com", (await SerialsAsync(admin))["example.com"]);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Empty(startup.Agent.Deletes);
        Assert.Equal(["example.com"], startup.Agent.ServedNames);
    }

    /// <summary>A zone the daemon serves that no row owns is removed, and the owned one beside it stays.</summary>
    [Fact]
    public async Task A_served_zone_with_no_row_is_removed()
    {
        using var admin = await WorldAsync("example.com");
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "example.com", (await SerialsAsync(admin))["example.com"]);
        await ServeAsync(startup.Agent, "orphan.example", 2026110700);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.True(settled);
        Assert.Equal(["orphan.example"], startup.Agent.Deletes);
        Assert.Equal(["example.com"], startup.Agent.ServedNames);
    }

    /// <summary>
    /// A served zone is checked for a row only after the name's lock is held, so a creation that had put it on
    /// the host and was waiting to commit its row is not removed as an orphan.
    /// </summary>
    [Fact]
    public async Task A_zone_whose_row_is_committed_while_the_name_lock_is_awaited_is_not_removed()
    {
        // The control is the test above: the same served zone with no row is removed when nothing intervenes.
        using var admin = await WorldAsync("example.com");
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "example.com", (await SerialsAsync(admin))["example.com"]);
        await ServeAsync(startup.Agent, "late.example", 2026110700);
        var zoneLock = new ObservingDnsZoneLock(startup.Database);
        zoneLock.OnNameGranted = async name =>
        {
            admin.Database.Zones.Add(new Zone(Guid.NewGuid(), AccountId, name, admin.Clock.UtcNow));
            await admin.Database.SaveChangesAsync();
        };
        using var scopes = new DnsReconcilerScopeFactory(startup.Database, startup.Agent, zoneLock);
        var reconciler = new DnsZoneReconciler(scopes.Scopes, new RecordingLogger<DnsZoneReconciler>());

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.True(settled);
        Assert.Equal(["late.example"], zoneLock.NamesLocked);
        Assert.Empty(startup.Agent.Deletes);
        Assert.Contains("late.example", startup.Agent.ServedNames);
    }

    /// <summary>Nothing is published or removed while DNS is not in service, whatever the rows and the daemon say.</summary>
    [Fact]
    public async Task Nothing_is_touched_while_dns_is_not_in_service()
    {
        // The accepting control is the test above: the same rows and the same orphan, with the server on.
        using var admin = new DnsTestContext();
        admin.Accounts.Add(AccountId, "dana", maxDnsZones: 10);
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "orphan.example", 2026110700);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.True(settled);
        Assert.Empty(startup.Agent.Puts);
        Assert.Empty(startup.Agent.Deletes);
        Assert.Equal(["orphan.example"], startup.Agent.ServedNames);
    }

    /// <summary>
    /// A daemon that cannot be asked what it serves fails the pass and removes nothing: an empty answer that
    /// was really an error would otherwise read as "every row's zone is missing and every zone is unowned".
    /// </summary>
    [Fact]
    public async Task A_listing_the_agent_refuses_fails_the_pass_and_removes_nothing()
    {
        using var admin = await WorldAsync("example.com");
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "orphan.example", 2026110700);
        startup.Agent.FailNext = Error.Of("AgentSystemFailure", ErrorType.Failure);
        var (reconciler, scopes) = Build(startup, startup.Agent);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.False(settled);
        Assert.Empty(startup.Agent.Puts);
        Assert.Empty(startup.Agent.Deletes);
    }

    /// <summary>One zone the host refuses does not stop the pass: the others are reconciled and the pass reports failure.</summary>
    [Fact]
    public async Task One_zone_the_host_refuses_does_not_stop_the_rest()
    {
        using var admin = await WorldAsync("a.example", "b.example", "c.example");
        using var startup = StartUp(admin);
        var logger = new RecordingLogger<DnsZoneReconciler>();
        var (reconciler, scopes) = Build(startup, new PutFailingDnsAgent(startup.Agent, "b.example"), logger);
        using var _ = scopes;

        var settled = await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.False(settled);
        Assert.Equal(["a.example", "c.example"], startup.Agent.ServedNames.Order().ToList());
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Level == LogLevel.Warning && entry.Message.Contains("b.example", StringComparison.Ordinal);
        });
    }

    /// <summary>A refused republication leaves the stored serial as it was, and the next zone's save does not carry it.</summary>
    [Fact]
    public async Task A_refused_republication_changes_no_stored_serial()
    {
        using var admin = await WorldAsync("a.example", "b.example");
        var before = await SerialsAsync(admin);
        using var startup = StartUp(admin);
        await ServeAsync(startup.Agent, "a.example", before["a.example"] + 50);
        await ServeAsync(startup.Agent, "b.example", before["b.example"] + 50);
        var (reconciler, scopes) = Build(startup, new PutFailingDnsAgent(startup.Agent, "a.example"));
        using var _ = scopes;

        await reconciler.ReconcileAsync(CancellationToken.None);

        var after = await SerialsAsync(admin);
        Assert.Equal(before["a.example"], after["a.example"]);
        Assert.True(after["b.example"] > before["b.example"] + 50);
    }
}
