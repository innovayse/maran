using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.DeleteZone;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Queries.GetZone;
using Maran.Modules.Dns.Queries.ListRecords;
using Maran.Modules.Dns.Queries.ListZones;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.Queries.Isolation;

/// <summary>
/// The tenant boundary: account A's zone is invisible to account B, to a reseller, and answers "not found"
/// (never "forbidden") on every read and on delete, while the owner and an administrator see it.
/// </summary>
public sealed class ZoneIsolationTests
{
    /// <summary>Account A.</summary>
    private static readonly Guid AccountA = new("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>Account B.</summary>
    private static readonly Guid AccountB = new("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>Builds an administrator's world holding one zone of account A with one record.</summary>
    /// <returns>The world and the zone's id.</returns>
    private static async Task<(DnsTestContext Admin, Guid ZoneId)> SeedAsync()
    {
        var admin = new DnsTestContext();
        admin.Accounts.Add(AccountA, "alice", maxDnsZones: 3);
        await admin.EnableDnsServerAsync();
        var zone = await admin.CreateZone.HandleAsync(
            new CreateZoneCommand(AccountA, "example.com", "203.0.113.1", "tests"), CancellationToken.None);
        await admin.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("www", RecordType.A, 300, "203.0.113.7"), zone.Value.Id, "203.0.113.1", "tests"),
            CancellationToken.None);

        return (admin, zone.Value.Id);
    }

    /// <summary>The owner and an administrator see the zone and its records.</summary>
    [Fact]
    public async Task The_owner_and_an_administrator_see_the_zone()
    {
        var (admin, zoneId) = await SeedAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(AccountA), admin.DatabaseName);
        owner.Accounts.Add(AccountA, "alice", 3);

        var seenByOwner = await owner.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listed = await owner.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);
        var records = await owner.ListRecords.HandleAsync(new ListRecordsQuery(ZoneId: zoneId), CancellationToken.None);

        Assert.True(seenByOwner.IsSuccess);
        Assert.Equal(["ns1.panel.test", "ns2.panel.test"], seenByOwner.Value.Nameservers);
        Assert.Equal(1, seenByOwner.Value.RecordCount);
        Assert.Single(listed.Value.Items);
        Assert.Equal("www", Assert.Single(records.Value.Items).Name);
    }

    /// <summary>Account B reading A's zone gets "not found" on get, on records, and an empty list.</summary>
    [Fact]
    public async Task Another_accounts_zone_is_invisible_and_not_found()
    {
        var (admin, zoneId) = await SeedAsync();
        using var _ = admin;
        using var intruder = new DnsTestContext(new CustomerCurrentUser(AccountB), admin.DatabaseName);

        var zone = await intruder.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listed = await intruder.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);
        var records = await intruder.ListRecords.HandleAsync(new ListRecordsQuery(ZoneId: zoneId), CancellationToken.None);

        Assert.Equal("ZoneNotFound", zone.Error!.Code);
        Assert.Equal(ErrorType.NotFound, zone.Error.Type);
        Assert.Empty(listed.Value.Items);
        Assert.Equal(0, listed.Value.Total);
        Assert.Equal("ZoneNotFound", records.Error!.Code);
    }

    /// <summary>Account B cannot delete A's zone, and nothing leaves the host.</summary>
    [Fact]
    public async Task Another_account_cannot_delete_the_zone()
    {
        var (admin, zoneId) = await SeedAsync();
        using var _ = admin;
        using var intruder = new DnsTestContext(new CustomerCurrentUser(AccountB), admin.DatabaseName);

        var result = await intruder.DeleteZone.HandleAsync(
            new DeleteZoneCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneNotFound", result.Error!.Code);
        Assert.Empty(intruder.Agent.Deletes);
        Assert.Single(admin.Database.Zones);
        Assert.False(Assert.Single(intruder.Entries).Succeeded);
    }

    /// <summary>A reseller, whose context shows no zone, gets "not found" too.</summary>
    [Fact]
    public async Task A_reseller_sees_no_zone()
    {
        var (admin, zoneId) = await SeedAsync();
        using var _ = admin;
        using var reseller = new DnsTestContext(new ResellerCurrentUser(), admin.DatabaseName);

        var zone = await reseller.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listed = await reseller.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);

        Assert.Equal("ZoneNotFound", zone.Error!.Code);
        Assert.Empty(listed.Value.Items);
    }

    /// <summary>The owner deleting the zone removes it from the host and the database.</summary>
    [Fact]
    public async Task The_owner_deleting_removes_the_zone_everywhere()
    {
        var (admin, zoneId) = await SeedAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(AccountA), admin.DatabaseName);
        owner.Agent.FailNext = null;

        var result = await owner.DeleteZone.HandleAsync(
            new DeleteZoneCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        // The owner's agent stand-in serves nothing, so it answers "not found", which deleting accepts.
        Assert.True(result.IsSuccess);
        Assert.Empty(admin.Database.Zones);
    }
}
