using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Commands.EnableZoneSigning;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.EnableZoneSigning;

/// <summary>
/// Switching DNSSEC on: the zone is published signed with a key the panel generated, the DS record to publish
/// at the registrar comes back, a host that cannot sign refuses it before anything changes, and the audit
/// entry names the zone and never the key.
/// </summary>
public sealed class EnableZoneSigningCommandHandlerTests
{
    /// <summary>Builds the command for a zone.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <returns>The command.</returns>
    private static EnableZoneSigningCommand Command(Guid zoneId)
    {
        return new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests");
    }

    /// <summary>Reads every stored key through a fresh administrator context, so nothing tracked is mistaken for stored.</summary>
    /// <param name="context">The world whose database is read.</param>
    /// <returns>The stored keys.</returns>
    private static async Task<List<ZoneSigningKey>> StoredKeysAsync(DnsTestContext context)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), context.DatabaseName);

        return await reader.Database.ZoneSigningKeys.AsNoTracking().ToListAsync();
    }

    /// <summary>Enabling signing publishes the zone signed with the stored key and answers with the DS record to publish.</summary>
    [Fact]
    public async Task Enabling_signing_publishes_the_zone_signed_with_the_stored_key_and_returns_the_ds_record()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;

        var result = await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var key = Assert.Single(await StoredKeysAsync(context));
        Assert.Equal(DnssecState.Signing, result.Value.State);
        Assert.True(result.Value.Available);
        Assert.False(result.Value.RolloverAutomatic);
        Assert.Equal(key.KeyTag, result.Value.Ds!.KeyTag);
        Assert.Equal(13, result.Value.Ds.Algorithm);
        Assert.Equal(2, result.Value.Ds.DigestType);
        Assert.Matches("^[0-9A-F]{64}$", result.Value.Ds.Digest);
        Assert.Equal((257, 3, 13, key.PublicKey), (
            result.Value.Dnskey!.Flags,
            result.Value.Dnskey.Protocol,
            result.Value.Dnskey.Algorithm,
            result.Value.Dnskey.PublicKey));

        var sent = context.Agent.Puts.Last();
        Assert.Equal(DnsSigningPhase.Active, sent.Signing!.Phase);
        Assert.Equal(key.PublicKey, sent.Signing.Key!.PublicKey);
        Assert.Equal(key.PrivateKey, sent.Signing.Key.PrivateKey.Reveal());
        Assert.Equal(key.KeyTag, sent.Signing.Key.KeyTag);
        Assert.Equal(257, sent.Signing.Key.Flags);
    }

    /// <summary>Enabling signing moves the zone's serial, like any change of what the zone publishes.</summary>
    [Fact]
    public async Task Enabling_signing_moves_the_serial()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        var before = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;

        await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        var after = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;
        Assert.True(after > before);
        Assert.Equal(after, context.Agent.Puts.Last().Serial);
    }

    /// <summary>The private key column is encrypted: the model stores it through a converter that does not leave it as it came.</summary>
    [Fact]
    public async Task The_private_key_column_is_stored_through_the_encrypting_converter()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        var column = context.Database.Model.FindEntityType(typeof(ZoneSigningKey))!.FindProperty(nameof(ZoneSigningKey.PrivateKey))!;
        var converter = column.GetValueConverter();
        var key = Assert.Single(await StoredKeysAsync(context));

        Assert.NotNull(converter);
        var stored = (string)converter.ConvertToProvider(key.PrivateKey)!;
        Assert.StartsWith(MarkingEncryptionService.Marker, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(key.PrivateKey, stored, StringComparison.Ordinal);
        Assert.Null(context.Database.Model.FindEntityType(typeof(ZoneSigningKey))!.FindProperty(nameof(ZoneSigningKey.PublicKey))!.GetValueConverter());
    }

    /// <summary>The audit entry names the zone and the action, and the key appears nowhere in it.</summary>
    [Fact]
    public async Task The_audit_entry_names_the_zone_and_never_the_key()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;

        await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        var entry = context.Entries[^1];
        var key = Assert.Single(await StoredKeysAsync(context));
        Assert.Equal(DnsAuditActions.ZoneSigningEnabled, entry.Action);
        Assert.Equal(DnssecWorld.ZoneName, entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(key.PrivateKey, $"{entry.Subject}{entry.Action}{entry.IpAddress}{entry.UserAgent}", StringComparison.Ordinal);
    }

    /// <summary>A host that cannot sign refuses before anything changes: no key, no publication, the zone still off.</summary>
    [Fact]
    public async Task A_host_that_cannot_sign_refuses_and_changes_nothing()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        context.Agent.DnssecSupported = false;
        var puts = context.Agent.Puts.Count;

        var result = await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningHostCannotSign", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(puts, context.Agent.Puts.Count);
        Assert.Empty(await StoredKeysAsync(context));
        Assert.Equal(DnssecState.Off, (await context.Database.Zones.AsNoTracking().SingleAsync()).DnssecState);
        Assert.False(context.Entries[^1].Succeeded);
        Assert.Equal(DnsAuditActions.ZoneSigningEnabled, context.Entries[^1].Action);
    }

    /// <summary>An agent that cannot be asked is its own failure and is not read as "cannot sign" or as "can".</summary>
    [Fact]
    public async Task An_agent_that_cannot_be_asked_answers_with_its_own_failure()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        context.Agent.FailNext = Error.Of("AgentUnavailable", ErrorType.Unavailable);

        var result = await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(await StoredKeysAsync(context));
    }

    /// <summary>A zone that is already signed refuses a second enabling and keeps the key it has.</summary>
    [Fact]
    public async Task A_zone_that_is_already_signed_refuses_a_second_enabling_and_keeps_its_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        var original = Assert.Single(await StoredKeysAsync(context));
        var puts = context.Agent.Puts.Count;

        var second = await context.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningNotOff", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(puts, context.Agent.Puts.Count);
        var stored = Assert.Single(await StoredKeysAsync(context));
        Assert.Equal(original.Id, stored.Id);
        Assert.Equal(original.PublicKey, stored.PublicKey);
    }

    /// <summary>A publication the agent refuses leaves no key behind and the zone off, so the next attempt starts clean.</summary>
    [Fact]
    public async Task A_publication_the_agent_refuses_leaves_no_key_and_the_zone_off()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        var refusing = new PutFailingDnsAgent(context.Agent, DnssecWorld.ZoneName);
        var handler = new EnableZoneSigningCommandHandler(
            new DnsZoneEditor(context.Database, refusing, context.ZoneLock),
            new DnssecHostProbe(refusing),
            new DnsAuditJournal(context.Audit, new AdminCurrentUser()),
            context.SigningClock);

        var result = await handler.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await StoredKeysAsync(context));
        using var reader = new DnsTestContext(new AdminCurrentUser(), context.DatabaseName);
        Assert.Equal(DnssecState.Off, (await reader.Database.Zones.AsNoTracking().SingleAsync()).DnssecState);
    }

    /// <summary>The zone's owner may switch signing on, and so may an administrator: both reach the same zone.</summary>
    [Fact]
    public async Task The_owner_may_enable_signing()
    {
        var (admin, zoneId) = await DnssecWorld.OffAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(DnssecWorld.AccountId), admin.DatabaseName);

        var result = await owner.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DnssecState.Signing, result.Value.State);
        Assert.Single(await StoredKeysAsync(admin));
    }

    /// <summary>Another account's zone answers not found, and no key is made for it.</summary>
    [Fact]
    public async Task Another_accounts_zone_answers_not_found_and_no_key_is_made()
    {
        var (admin, zoneId) = await DnssecWorld.OffAsync();
        using var _ = admin;
        using var intruder = new DnsTestContext(new CustomerCurrentUser(Guid.NewGuid()), admin.DatabaseName);

        var result = await intruder.EnableSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(await StoredKeysAsync(admin));
        Assert.Empty(intruder.Agent.Puts);
    }
}
