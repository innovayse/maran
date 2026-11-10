using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Queries.GetZoneDnssec;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.Queries.GetZoneDnssec;

/// <summary>
/// Reading a zone's DNSSEC situation: the row beside what the host is seen to be able to do, the DS record to
/// publish while the zone is signed or being withdrawn, an agent that cannot be asked read as "cannot sign" and
/// not as a failure, and another account's zone answering 404.
/// </summary>
public sealed class GetZoneDnssecQueryHandlerTests
{
    /// <summary>A zone that was never signed reports off, available on a host that can sign, with no DS record.</summary>
    [Fact]
    public async Task An_unsigned_zone_reports_off_and_available_with_no_ds_record()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.Equal(DnssecState.Off, result.Value.State);
        Assert.True(result.Value.Available);
        Assert.Null(result.Value.Ds);
        Assert.Null(result.Value.Dnskey);
        Assert.Null(result.Value.Observed);
        Assert.False(result.Value.RolloverAutomatic);
    }

    /// <summary>On a host that cannot sign the answer says so: this is what hides DNSSEC on such a host.</summary>
    [Fact]
    public async Task A_host_that_cannot_sign_reports_not_available()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        context.Agent.DnssecSupported = false;

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
    }

    /// <summary>An agent that cannot be asked reads as "cannot sign", and the query still answers.</summary>
    [Fact]
    public async Task An_agent_that_cannot_be_asked_reads_as_not_available_and_not_as_a_failure()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        context.Agent.FailNext = Error.Of("AgentUnavailable", ErrorType.Unavailable);

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Null(result.Value.Observed);
        Assert.NotNull(result.Value.Ds);
    }

    /// <summary>A signed zone reports its DS record and DNSKEY from the row, the DS being the digest of that very key.</summary>
    [Fact]
    public async Task A_signed_zone_reports_the_ds_record_and_dnskey_of_its_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        var sentKey = context.Agent.Puts.Last().Signing!.Key!;

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.Equal(DnssecState.Signing, result.Value.State);
        Assert.Equal(sentKey.KeyTag, result.Value.Ds!.KeyTag);
        Assert.Equal(sentKey.PublicKey, result.Value.Dnskey!.PublicKey);
        Assert.Equal(DnsSigningPhase.Active, context.Agent.Puts.Last().Signing!.Phase);
        Assert.Null(result.Value.WithdrawalStartedAt);
    }

    /// <summary>What the daemon reports about the zone is carried beside the row, fact by fact.</summary>
    [Fact]
    public async Task What_the_daemon_reports_is_carried_beside_the_row()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        context.Agent.Observation = new DnsSigningObservedDto(
            DnsSigningPhase.Active, DnsPolicyState.InForce, 4242, DnsPolicyState.NotInForce);

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        var observed = result.Value.Observed!;
        Assert.Equal(DnssecState.Signing, observed.Mode);
        Assert.Equal(DnsPolicyState.InForce, observed.ModeObserved);
        Assert.Equal(4242, observed.PublishedKeyTag);
        Assert.Equal(DnsPolicyState.NotInForce, observed.PublishedKeyObserved);
    }

    /// <summary>A zone that is being withdrawn still reports the DS record to remove and when the withdrawal may end.</summary>
    [Fact]
    public async Task A_withdrawing_zone_reports_the_ds_record_to_remove_and_the_earliest_finish()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;

        var result = await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.Equal(DnssecState.Withdrawing, result.Value.State);
        Assert.NotNull(result.Value.Ds);
        Assert.Equal(context.SigningClock.UtcNow.AddHours(48), result.Value.WithdrawalEarliestFinishAt);
    }

    /// <summary>Another account's zone answers not found, never forbidden, and the owner and an administrator see it.</summary>
    [Fact]
    public async Task Another_accounts_zone_answers_not_found_and_the_owner_sees_it()
    {
        var (admin, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(DnssecWorld.AccountId), admin.DatabaseName);
        using var intruder = new DnsTestContext(new CustomerCurrentUser(Guid.NewGuid()), admin.DatabaseName);
        using var reseller = new DnsTestContext(new ResellerCurrentUser(), admin.DatabaseName);

        var seenByOwner = await owner.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);
        var seenByIntruder = await intruder.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);
        var seenByReseller = await reseller.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None);

        Assert.True(seenByOwner.IsSuccess);
        Assert.NotNull(seenByOwner.Value.Ds);
        Assert.Equal("ZoneNotFound", seenByIntruder.Error!.Code);
        Assert.Equal(ErrorType.NotFound, seenByIntruder.Error.Type);
        Assert.Equal("ZoneNotFound", seenByReseller.Error!.Code);
    }
}
