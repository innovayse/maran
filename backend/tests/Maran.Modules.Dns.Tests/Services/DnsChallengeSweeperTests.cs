using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Services;

/// <summary>
/// The sweep of challenge records an order left behind: an old one is removed, a fresh one and a customer's own
/// record at the same name are kept, nothing happens while the name server is off, and the sweep runs with no
/// principal.
/// </summary>
public sealed class DnsChallengeSweeperTests
{
    /// <summary>The account that owns the zone.</summary>
    private static readonly Guid AccountId = new("44444444-4444-4444-8444-444444444444");

    /// <summary>Builds a challenge draft at the apex's challenge name.</summary>
    /// <param name="fill">The character the forty-three characters of the value repeat.</param>
    /// <returns>The draft.</returns>
    private static RecordDraft ChallengeDraft(char fill)
    {
        return new RecordDraft(ChallengeRecordPolicy.Label, RecordType.Txt, DnsLimits.MinTtl, new string(fill, 43));
    }

    /// <summary>Seeds a zone holding an old challenge record, a fresh one and a customer's old TXT at the same name.</summary>
    /// <returns>The world, with the name server on.</returns>
    private static async Task<DnsTestContext> SeededAsync()
    {
        var admin = new DnsTestContext();
        admin.Accounts.Add(AccountId, "alice", maxDnsZones: 10);
        await admin.EnableDnsServerAsync();
        var now = admin.Clock.UtcNow;
        var zone = new Zone(Guid.NewGuid(), AccountId, "example.com", now.AddDays(-1));
        zone.AddChallengeRecord(Guid.NewGuid(), ChallengeDraft('a'), now.AddHours(-3));
        zone.AddChallengeRecord(Guid.NewGuid(), ChallengeDraft('b'), now.AddMinutes(-5));
        zone.AddRecord(
            Guid.NewGuid(),
            new RecordDraft(ChallengeRecordPolicy.Label, RecordType.Txt, 300, "customer text"),
            maxRecords: 10,
            now.AddDays(-1));
        admin.Database.Zones.Add(zone);
        await admin.Database.SaveChangesAsync();

        return admin;
    }

    /// <summary>Builds the sweeper over the start-up caller's view of the same database.</summary>
    /// <param name="admin">The world that seeded the rows.</param>
    /// <returns>The sweeper, the start-up world and the container that must outlive both.</returns>
    private static (DnsChallengeSweeper Sweeper, DnsTestContext World, DnsReconcilerScopeFactory Scopes) Build(DnsTestContext admin)
    {
        var world = new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
        var scopes = new DnsReconcilerScopeFactory(world.Database, world.Agent);

        return (new DnsChallengeSweeper(scopes.Scopes, new RecordingLogger<DnsChallengeSweeper>()), world, scopes);
    }

    /// <summary>Reads the stored values of the zone's records, through a fresh administrator context.</summary>
    /// <param name="admin">The world whose database is read.</param>
    /// <returns>The record values.</returns>
    private static async Task<List<string>> ValuesAsync(DnsTestContext admin)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), admin.DatabaseName);
        var zone = await reader.Database.Zones.Include(row => row.Records).SingleAsync();

        return zone.Records.Select(record =>
        {
            return record.Value;
        }).ToList();
    }

    /// <summary>The sweep removes the old challenge record and keeps the fresh one and the customer's own.</summary>
    [Fact]
    public async Task The_sweep_removes_the_old_challenge_record_and_keeps_the_fresh_one_and_the_customers()
    {
        using var admin = await SeededAsync();
        var (sweeper, world, scopes) = Build(admin);
        using var scopeFactory = scopes;
        using var worldScope = world;

        var swept = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(1, swept);
        var values = await ValuesAsync(admin);
        Assert.DoesNotContain(new string('a', 43), values);
        Assert.Contains(new string('b', 43), values);
        Assert.Contains("customer text", values);
        Assert.Single(world.Agent.Puts);
    }

    /// <summary>A second pass finds nothing and tells the agent nothing.</summary>
    [Fact]
    public async Task A_second_pass_finds_nothing_and_tells_the_agent_nothing()
    {
        using var admin = await SeededAsync();
        var (sweeper, world, scopes) = Build(admin);
        using var scopeFactory = scopes;
        using var worldScope = world;
        await sweeper.SweepAsync(CancellationToken.None);
        var putsAfterFirst = world.Agent.Puts.Count;

        var swept = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(0, swept);
        Assert.Equal(putsAfterFirst, world.Agent.Puts.Count);
    }

    /// <summary>Nothing is swept while the name server is switched off.</summary>
    [Fact]
    public async Task Nothing_is_swept_while_the_name_server_is_switched_off()
    {
        using var admin = await SeededAsync();
        var settings = await admin.Database.DnsSettings.SingleAsync();
        settings.Disable(admin.Clock.UtcNow);
        await admin.Database.SaveChangesAsync();
        var (sweeper, world, scopes) = Build(admin);
        using var scopeFactory = scopes;
        using var worldScope = world;

        var swept = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(0, swept);
        Assert.Contains(new string('a', 43), await ValuesAsync(admin));
        Assert.Empty(world.Agent.Puts);
    }
}
