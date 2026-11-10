using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Tests.TestSupport;

namespace Maran.Modules.Dns.Tests.Commands.EditRecords;

/// <summary>
/// An ordinary edit of a zone that is signed publishes the zone signed with the same key: an edit that left the
/// signing out of the publication would turn the zone unsigned while the registrar still holds its DS record.
/// </summary>
public sealed class EditSignedZoneTests
{
    /// <summary>A record added to a signed zone is published with the signed phase and the stored key.</summary>
    [Fact]
    public async Task A_record_added_to_a_signed_zone_is_published_signed_with_the_same_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        var key = context.Agent.Puts.Last().Signing!.Key!;

        var added = await context.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("www", RecordType.A, 300, "203.0.113.7"), zoneId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.True(added.IsSuccess);
        var sent = context.Agent.Puts.Last();
        Assert.Equal("www", Assert.Single(sent.Records).Name);
        Assert.Equal(DnsSigningPhase.Active, sent.Signing!.Phase);
        Assert.Equal(key.PublicKey, sent.Signing.Key!.PublicKey);
        Assert.Equal(key.PrivateKey.Reveal(), sent.Signing.Key.PrivateKey.Reveal());
    }

    /// <summary>Inverse control: the same edit of a zone that was never signed carries no signing instruction.</summary>
    [Fact]
    public async Task A_record_added_to_an_unsigned_zone_is_published_without_signing()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;

        await context.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("www", RecordType.A, 300, "203.0.113.7"), zoneId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.Null(context.Agent.Puts.Last().Signing);
    }
}
