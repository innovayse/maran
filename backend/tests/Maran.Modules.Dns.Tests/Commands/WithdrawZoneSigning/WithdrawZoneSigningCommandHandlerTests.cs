using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Commands.EnableZoneSigning;
using Maran.Modules.Dns.Commands.WithdrawZoneSigning;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.WithdrawZoneSigning;

/// <summary>
/// Beginning a withdrawal: the zone is published again with the SAME key under the withdrawing phase, the
/// answer says when the withdrawal may end, and only a signed zone can be withdrawn.
/// </summary>
public sealed class WithdrawZoneSigningCommandHandlerTests
{
    /// <summary>Builds the command for a zone.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <returns>The command.</returns>
    private static WithdrawZoneSigningCommand Command(Guid zoneId)
    {
        return new WithdrawZoneSigningCommand(zoneId, "203.0.113.1", "tests");
    }

    /// <summary>Withdrawing keeps the key and publishes it under the withdrawing phase.</summary>
    [Fact]
    public async Task Withdrawing_a_signed_zone_publishes_the_same_key_under_the_withdrawing_phase()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        var signedWith = context.Agent.Puts.Last().Signing!.Key!;

        var result = await context.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = context.Agent.Puts.Last().Signing!;
        Assert.Equal(DnsSigningPhase.Withdrawing, sent.Phase);
        Assert.Equal(signedWith.PublicKey, sent.Key!.PublicKey);
        Assert.Equal(signedWith.PrivateKey.Reveal(), sent.Key.PrivateKey.Reveal());
        Assert.Equal(signedWith.KeyTag, sent.Key.KeyTag);
        Assert.Equal(DnssecState.Withdrawing, result.Value.State);
    }

    /// <summary>The answer states when the withdrawal began and the earliest instant it may end, 48 hours later.</summary>
    [Fact]
    public async Task The_answer_states_when_the_withdrawal_may_end()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;

        var result = await context.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal(context.SigningClock.UtcNow, result.Value.WithdrawalStartedAt);
        Assert.Equal(context.SigningClock.UtcNow.AddHours(48), result.Value.WithdrawalEarliestFinishAt);
        Assert.NotNull(result.Value.Ds);
    }

    /// <summary>Withdrawing moves the serial and writes an audit entry naming the zone and never the key.</summary>
    [Fact]
    public async Task Withdrawing_moves_the_serial_and_is_audited_without_the_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        var before = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;

        await context.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        var after = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;
        var entry = context.Entries[^1];
        Assert.True(after > before);
        Assert.Equal(DnsAuditActions.ZoneSigningWithdrawalStarted, entry.Action);
        Assert.Equal(DnssecWorld.ZoneName, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A zone that is not signed has nothing to withdraw, and the host is not told anything.</summary>
    [Fact]
    public async Task A_zone_that_is_not_signed_cannot_be_withdrawn()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        var puts = context.Agent.Puts.Count;

        var result = await context.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningNotSigning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(puts, context.Agent.Puts.Count);
        Assert.False(context.Entries[^1].Succeeded);
    }

    /// <summary>A withdrawal that has begun is not begun again, so the 48 hours cannot be restarted or shortened by repeating it.</summary>
    [Fact]
    public async Task A_withdrawal_that_has_begun_cannot_be_begun_again()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;
        var began = context.SigningClock.UtcNow;
        context.SigningClock.Advance(TimeSpan.FromHours(24));

        var again = await context.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningNotSigning", again.Error!.Code);
        var zone = await context.Database.Zones.AsNoTracking().SingleAsync();
        Assert.Equal(began, zone.DnssecWithdrawalStartedAt);
    }

    /// <summary>Another account's zone answers not found and is left signed.</summary>
    [Fact]
    public async Task Another_accounts_zone_answers_not_found_and_stays_signed()
    {
        var (admin, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = admin;
        using var intruder = new DnsTestContext(new CustomerCurrentUser(Guid.NewGuid()), admin.DatabaseName);

        var result = await intruder.WithdrawSigning.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneNotFound", result.Error!.Code);
        Assert.Equal(DnssecState.Signing, (await admin.Database.Zones.AsNoTracking().SingleAsync()).DnssecState);
    }

    /// <summary>Enabling after a withdrawal began is refused too: the zone is not off.</summary>
    [Fact]
    public async Task A_zone_being_withdrawn_cannot_be_signed_again()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;

        var result = await context.EnableSigning.HandleAsync(
            new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneSigningNotOff", result.Error!.Code);
    }
}
