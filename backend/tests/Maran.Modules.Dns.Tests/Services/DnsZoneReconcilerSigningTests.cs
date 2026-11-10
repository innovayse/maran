using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Services;

/// <summary>
/// The most important integration of DNSSEC: a host that was rebuilt is handed the stored signing config with
/// every zone. If the start-up pass published a signed zone unsigned while the registrar still holds its DS
/// record, every validating resolver would fail the domain. The pass runs as the start-up caller (no principal,
/// so the tenant filter shows it nothing) against a fresh agent that serves nothing, which is a rebuilt host.
/// </summary>
public sealed class DnsZoneReconcilerSigningTests
{
    /// <summary>Builds the start-up caller's view of the same database: an anonymous principal and an agent serving nothing.</summary>
    /// <param name="admin">The world that holds the rows.</param>
    /// <returns>The start-up world.</returns>
    private static DnsTestContext RebuiltHost(DnsTestContext admin)
    {
        return new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
    }

    /// <summary>Reads the stored key of the world's one zone, through a fresh administrator context.</summary>
    /// <param name="admin">The world whose database is read.</param>
    /// <returns>The stored key.</returns>
    private static async Task<ZoneSigningKey> StoredKeyAsync(DnsTestContext admin)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), admin.DatabaseName);

        return await reader.Database.ZoneSigningKeys.AsNoTracking().SingleAsync();
    }

    /// <summary>Runs one pass of the reconciler for a rebuilt host.</summary>
    /// <param name="host">The rebuilt host's world.</param>
    /// <returns>Whether the pass left nothing failed.</returns>
    private static async Task<bool> ReconcileAsync(DnsTestContext host)
    {
        using var scopes = new DnsReconcilerScopeFactory(host.Database, host.Agent);
        var reconciler = new DnsZoneReconciler(scopes.Scopes, new RecordingLogger<DnsZoneReconciler>());

        return await reconciler.ReconcileAsync(CancellationToken.None);
    }

    /// <summary>A rebuilt host is given the zone signed, with the key the registrar's DS record names.</summary>
    [Fact]
    public async Task A_rebuilt_host_is_given_the_signed_zone_with_the_stored_key()
    {
        var (admin, _) = await DnssecWorld.SigningAsync();
        using var _a = admin;
        var stored = await StoredKeyAsync(admin);
        using var host = RebuiltHost(admin);

        var settled = await ReconcileAsync(host);

        Assert.True(settled);
        var sent = Assert.Single(host.Agent.Puts);
        Assert.Equal(DnssecWorld.ZoneName, sent.Name);
        Assert.Equal(DnsSigningPhase.Active, sent.Signing!.Phase);
        Assert.Equal(stored.PublicKey, sent.Signing.Key!.PublicKey);
        Assert.Equal(stored.PrivateKey, sent.Signing.Key.PrivateKey.Reveal());
        Assert.Equal(stored.KeyTag, sent.Signing.Key.KeyTag);
        Assert.Equal(257, sent.Signing.Key.Flags);
    }

    /// <summary>A rebuilt host is given a zone that is being withdrawn under the withdrawing phase and the same key.</summary>
    [Fact]
    public async Task A_rebuilt_host_is_given_a_withdrawing_zone_with_the_same_key()
    {
        var (admin, _) = await DnssecWorld.WithdrawingAsync();
        using var _a = admin;
        var stored = await StoredKeyAsync(admin);
        using var host = RebuiltHost(admin);

        await ReconcileAsync(host);

        var sent = Assert.Single(host.Agent.Puts);
        Assert.Equal(DnsSigningPhase.Withdrawing, sent.Signing!.Phase);
        Assert.Equal(stored.PublicKey, sent.Signing.Key!.PublicKey);
    }

    /// <summary>Inverse control: a rebuilt host is given a zone that was never signed with no signing instruction at all.</summary>
    [Fact]
    public async Task A_rebuilt_host_is_given_an_unsigned_zone_with_no_signing_instruction()
    {
        var (admin, _) = await DnssecWorld.OffAsync();
        using var _a = admin;
        using var host = RebuiltHost(admin);

        await ReconcileAsync(host);

        Assert.Null(Assert.Single(host.Agent.Puts).Signing);
    }

    /// <summary>
    /// A zone the daemon serves behind its row is republished signed too, so correcting a stale serial can never
    /// turn a signed zone into an unsigned one.
    /// </summary>
    [Fact]
    public async Task A_signed_zone_served_behind_its_row_is_republished_signed()
    {
        var (admin, _) = await DnssecWorld.SigningAsync();
        using var _a = admin;
        using var host = RebuiltHost(admin);
        await host.Agent.PutZoneAsync(
            new DnsZoneDefinition(DnssecWorld.ZoneName, 1, "ns1.panel.test", "hostmaster.panel.test", ["ns1.panel.test"], []),
            CancellationToken.None);
        host.Agent.Puts.Clear();

        await ReconcileAsync(host);

        Assert.Equal(DnsSigningPhase.Active, Assert.Single(host.Agent.Puts).Signing!.Phase);
    }
}
