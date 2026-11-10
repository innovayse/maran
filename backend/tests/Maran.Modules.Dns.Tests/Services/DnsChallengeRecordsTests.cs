using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Services;

/// <summary>
/// The DNS module's challenge door, driven the way the certificate module drives it: with no signed-in user,
/// naming the account. Every refusal is paired with the acceptance that proves the door can say yes, and the
/// tenant tests use two accounts that each own a zone.
/// </summary>
public sealed class DnsChallengeRecordsTests
{
    /// <summary>The account whose zone the challenge is published into.</summary>
    private static readonly Guid AccountA = new("44444444-4444-4444-8444-444444444444");

    /// <summary>The account that owns the other zone.</summary>
    private static readonly Guid AccountB = new("55555555-5555-4555-8555-555555555555");

    /// <summary>A digest-shaped value.</summary>
    private static readonly string ValueOne = new('a', 43);

    /// <summary>A second digest-shaped value.</summary>
    private static readonly string ValueTwo = new('b', 43);

    /// <summary>A third digest-shaped value.</summary>
    private static readonly string ValueThree = new('c', 43);

    /// <summary>Builds an administrator's world: the name server on, one zone for each of two accounts.</summary>
    /// <returns>The world that seeded the rows.</returns>
    private static async Task<DnsTestContext> SeededAsync()
    {
        var admin = new DnsTestContext();
        admin.Accounts.Add(AccountA, "alice", maxDnsZones: 10);
        admin.Accounts.Add(AccountB, "bruno", maxDnsZones: 10);
        await admin.EnableDnsServerAsync();
        foreach (var (account, name) in new[] { (AccountA, "example.com"), (AccountB, "other.org") })
        {
            var created = await admin.CreateZone.HandleAsync(
                new CreateZoneCommand(account, name, "203.0.113.1", "tests"), CancellationToken.None);
            Assert.True(created.IsSuccess);
        }

        return admin;
    }

    /// <summary>The door as the certificate module uses it: an anonymous caller over the same database.</summary>
    /// <param name="admin">The world that seeded the rows.</param>
    /// <param name="locks">Receives the lock stand-in, so a test can count what is held.</param>
    /// <returns>The unattended world and the door built over it.</returns>
    private static (DnsTestContext World, IDnsChallengeRecords Door) Unattended(
        DnsTestContext admin,
        out CountingDnsZoneLock locks)
    {
        var world = new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
        locks = new CountingDnsZoneLock(world.Database);
        var editor = new DnsZoneEditor(world.Database, world.Agent, locks);

        return (world, new DnsChallengeRecords(world.Database, editor, world.Clock));
    }

    /// <summary>Reads the records of the zone with a name, through a fresh administrator context.</summary>
    /// <param name="admin">The world whose database is read.</param>
    /// <param name="zoneName">The apex.</param>
    /// <returns>The zone's stored records.</returns>
    private static async Task<List<ZoneRecord>> RecordsOfAsync(DnsTestContext admin, string zoneName)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), admin.DatabaseName);
        var zone = await reader.Database.Zones.Include(row => row.Records).SingleAsync(row => row.Name == zoneName);

        return zone.Records.ToList();
    }

    /// <summary>Publishing for a name in the account's own zone stores a challenge TXT and tells the agent.</summary>
    [Fact]
    public async Task Publishing_in_the_accounts_own_zone_stores_a_challenge_record_and_tells_the_agent()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, "www.example.com", ValueOne, CancellationToken.None);

        Assert.True(published.IsSuccess, published.IsSuccess ? string.Empty : published.Error!.Code);
        var stored = Assert.Single(await RecordsOfAsync(admin, "example.com"));
        Assert.Equal("_acme-challenge.www", stored.Name);
        Assert.Equal(RecordType.Txt, stored.Type);
        Assert.Equal(DnsLimits.MinTtl, stored.Ttl);
        Assert.Equal(ValueOne, stored.Value);
        Assert.Equal(RecordOrigin.AcmeChallenge, stored.Origin);
        var sent = Assert.Single(world.Agent.Puts);
        var record = Assert.Single(sent.Records);
        Assert.Equal("_acme-challenge.www", record.Name);
        Assert.Equal(ValueOne, record.Value);
        Assert.Equal(new DnsChallengeHandle(AccountA, stored.ZoneId, stored.Id), published.Value);
    }

    /// <summary>The apex name lands on the bare challenge label.</summary>
    [Fact]
    public async Task A_name_at_the_apex_lands_on_the_bare_challenge_label()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);

        Assert.True(published.IsSuccess);
        Assert.Equal("_acme-challenge", Assert.Single(await RecordsOfAsync(admin, "example.com")).Name);
    }

    /// <summary>A name inside another account's zone is a name with no zone, and nothing is written.</summary>
    [Fact]
    public async Task A_name_inside_another_accounts_zone_is_a_name_with_no_zone()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, "host.other.org", ValueOne, CancellationToken.None);

        Assert.False(published.IsSuccess);
        Assert.Equal("DnsChallengeZoneNotFound", published.Error!.Code);
        Assert.Equal(ErrorType.NotFound, published.Error.Type);
        Assert.Empty(world.Agent.Puts);
        Assert.Empty(await RecordsOfAsync(admin, "other.org"));

        // The accepting control: the account that owns that zone may publish into it.
        var own = await door.PublishAsync(AccountB, "host.other.org", ValueOne, CancellationToken.None);
        Assert.True(own.IsSuccess);
    }

    /// <summary>An anonymous caller reaches only the zones of the account it names.</summary>
    [Fact]
    public async Task An_anonymous_caller_reaches_only_the_zones_of_the_account_it_names()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        // The tenant filter shows an anonymous caller nothing at all; the account argument is what scopes.
        Assert.Empty(await world.Database.Zones.ToListAsync());

        var named = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);
        var other = await door.PublishAsync(Guid.NewGuid(), "example.com", ValueOne, CancellationToken.None);

        Assert.True(named.IsSuccess);
        Assert.Equal("DnsChallengeZoneNotFound", other.Error!.Code);
    }

    /// <summary>Only a digest-shaped value is published; a SPF line is refused before anything is written.</summary>
    [Fact]
    public async Task Only_a_digest_shaped_value_is_published()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var spf = await door.PublishAsync(AccountA, "example.com", "v=spf1 -all", CancellationToken.None);
        var shortValue = await door.PublishAsync(AccountA, "example.com", new string('a', 42), CancellationToken.None);

        Assert.Equal("DnsChallengeValueInvalid", spf.Error!.Code);
        Assert.Equal("DnsChallengeValueInvalid", shortValue.Error!.Code);
        Assert.Empty(world.Agent.Puts);
        Assert.Empty(await RecordsOfAsync(admin, "example.com"));

        // The accepting control: a forty-three character value passes the same door.
        Assert.True((await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None)).IsSuccess);
    }

    /// <summary>A name with more than one spelling, or the challenge label itself, is refused.</summary>
    [Theory]
    [InlineData("*.example.com")]
    [InlineData("example.com.")]
    [InlineData("Example.com")]
    [InlineData("_acme-challenge.example.com")]
    [InlineData("example")]
    public async Task A_name_with_more_than_one_spelling_is_refused(string hostname)
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, hostname, ValueOne, CancellationToken.None);

        Assert.Equal("DnsChallengeHostnameInvalid", published.Error!.Code);
        Assert.Equal(ErrorType.Validation, published.Error.Type);
        Assert.Empty(world.Agent.Puts);
    }

    /// <summary>Two values sit at one name and a third is refused with nothing written.</summary>
    [Fact]
    public async Task Two_values_sit_at_one_name_and_a_third_is_refused()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;
        Assert.True((await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None)).IsSuccess);
        Assert.True((await door.PublishAsync(AccountA, "example.com", ValueTwo, CancellationToken.None)).IsSuccess);
        var putsBefore = world.Agent.Puts.Count;

        var third = await door.PublishAsync(AccountA, "example.com", ValueThree, CancellationToken.None);

        Assert.Equal("DnsChallengeValuesFull", third.Error!.Code);
        Assert.Equal(putsBefore, world.Agent.Puts.Count);
        Assert.Equal(2, (await RecordsOfAsync(admin, "example.com")).Count);
    }

    /// <summary>A challenge record does not count against the zone's record cap.</summary>
    [Fact]
    public async Task A_challenge_record_does_not_count_against_the_zone_record_cap()
    {
        using var admin = await SeededAsync();
        var zoneId = (await admin.Database.Zones.SingleAsync(row => row.Name == "example.com")).Id;
        foreach (var name in new[] { "a", "b", "c" })
        {
            var added = await admin.AddRecord.HandleAsync(
                new AddRecordCommand(new RecordDraft(name, RecordType.A, 300, "203.0.113.7"), zoneId), CancellationToken.None);
            Assert.True(added.IsSuccess);
        }

        // The accepting control: the cap (three) is reached, so a customer's fourth is refused.
        var fourth = await admin.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("d", RecordType.A, 300, "203.0.113.7"), zoneId), CancellationToken.None);
        Assert.Equal("ZoneRecordLimitReached", fourth.Error!.Code);
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);

        Assert.True(published.IsSuccess);
    }

    /// <summary>Removing a published record takes it out of the row and out of what the agent serves.</summary>
    [Fact]
    public async Task Removing_a_published_record_takes_it_out_of_the_row_and_the_agent()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;
        var handle = (await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None)).Value;

        var removed = await door.RemoveAsync(handle, CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.Empty(await RecordsOfAsync(admin, "example.com"));
        Assert.Empty(world.Agent.Puts[^1].Records);
    }

    /// <summary>Removing twice is the same success, because the first removal already did what was asked.</summary>
    [Fact]
    public async Task Removing_twice_is_the_same_success()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;
        var handle = (await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None)).Value;
        await door.RemoveAsync(handle, CancellationToken.None);

        var again = await door.RemoveAsync(handle, CancellationToken.None);

        Assert.True(again.IsSuccess);
    }

    /// <summary>A handle that names another account's zone removes nothing there.</summary>
    [Fact]
    public async Task A_handle_naming_another_accounts_zone_removes_nothing_there()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;
        var handle = (await door.PublishAsync(AccountB, "other.org", ValueOne, CancellationToken.None)).Value;

        var forged = await door.RemoveAsync(handle with { AccountId = AccountA }, CancellationToken.None);

        Assert.True(forged.IsSuccess);
        Assert.Single(await RecordsOfAsync(admin, "other.org"));

        // The accepting control: the genuine handle does remove it.
        Assert.True((await door.RemoveAsync(handle, CancellationToken.None)).IsSuccess);
        Assert.Empty(await RecordsOfAsync(admin, "other.org"));
    }

    /// <summary>The door never removes a customer's record, even one at the challenge name.</summary>
    [Fact]
    public async Task The_door_never_removes_a_customers_record()
    {
        using var admin = await SeededAsync();
        var zone = await admin.Database.Zones.SingleAsync(row => row.Name == "example.com");
        var added = await admin.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("_acme-challenge", RecordType.Txt, 300, "customer text"), zone.Id),
            CancellationToken.None);
        Assert.True(added.IsSuccess);
        var customerId = Assert.Single(await RecordsOfAsync(admin, "example.com")).Id;
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var removed = await door.RemoveAsync(new DnsChallengeHandle(AccountA, zone.Id, customerId), CancellationToken.None);

        Assert.Equal("RecordNotEditable", removed.Error!.Code);
        Assert.Single(await RecordsOfAsync(admin, "example.com"));
    }

    /// <summary>The zone's lock is free once the publish has returned, so it is free while the authority validates.</summary>
    [Fact]
    public async Task The_zone_lock_is_free_between_the_publish_and_the_removal()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out var locks);
        using var scope = world;

        var published = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);

        // Between this line and the removal below is the whole validation, which can take minutes.
        Assert.Equal(0, locks.Held);

        await door.RemoveAsync(published.Value, CancellationToken.None);
        Assert.Equal(0, locks.Held);
    }

    /// <summary>A publish the agent refuses writes nothing and releases the lock.</summary>
    [Fact]
    public async Task A_publish_the_agent_refuses_writes_nothing_and_releases_the_lock()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out var locks);
        using var scope = world;
        world.Agent.FailNext = Error.Of("AgentUnavailable", ErrorType.Unavailable);

        var published = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);

        Assert.Equal("AgentUnavailable", published.Error!.Code);
        Assert.Empty(await RecordsOfAsync(admin, "example.com"));
        Assert.Equal(0, locks.Held);
    }

    /// <summary>The check says available for a served name and no zone for another account's.</summary>
    [Fact]
    public async Task The_check_says_available_for_a_served_name_and_no_zone_for_another_accounts()
    {
        using var admin = await SeededAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        Assert.Equal(DnsChallengeAvailability.Available, await door.CheckAsync(AccountA, "www.example.com", CancellationToken.None));
        Assert.Equal(DnsChallengeAvailability.NoZone, await door.CheckAsync(AccountA, "host.other.org", CancellationToken.None));
        Assert.Equal(DnsChallengeAvailability.NoZone, await door.CheckAsync(AccountA, "*.example.com", CancellationToken.None));
    }

    /// <summary>A zone the account owns is not available while the name server is switched off.</summary>
    [Fact]
    public async Task A_zone_is_not_available_while_the_name_server_is_switched_off()
    {
        using var admin = await SeededAsync();
        var settings = await admin.Database.DnsSettings.SingleAsync();
        settings.Disable(admin.Clock.UtcNow);
        await admin.Database.SaveChangesAsync();
        var (world, door) = Unattended(admin, out _);
        using var scope = world;

        var availability = await door.CheckAsync(AccountA, "example.com", CancellationToken.None);
        var published = await door.PublishAsync(AccountA, "example.com", ValueOne, CancellationToken.None);

        Assert.Equal(DnsChallengeAvailability.DnsNotEnabled, availability);
        Assert.Equal("DnsNotEnabled", published.Error!.Code);
    }
}
