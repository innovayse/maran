using Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.FinishZoneSigningWithdrawal;

/// <summary>
/// Ending a withdrawal, the one step that cannot be taken back: refused before 48 hours have passed for every
/// caller, an administrator included, allowed after them with the injected clock, and then the zone is unsigned
/// and its key is gone. The clock is moved by the test and nothing sleeps.
/// </summary>
public sealed class FinishZoneSigningWithdrawalCommandHandlerTests
{
    /// <summary>Builds the command for a zone with the statement that the DS record was removed.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <returns>The command.</returns>
    private static FinishZoneSigningWithdrawalCommand Command(Guid zoneId)
    {
        return new FinishZoneSigningWithdrawalCommand(true, zoneId, "203.0.113.1", "tests");
    }

    /// <summary>Counts the keys stored, through a fresh administrator context.</summary>
    /// <param name="context">The world whose database is read.</param>
    /// <returns>How many keys are stored.</returns>
    private static async Task<int> StoredKeyCountAsync(DnsTestContext context)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), context.DatabaseName);

        return await reader.Database.ZoneSigningKeys.AsNoTracking().CountAsync();
    }

    /// <summary>An administrator is refused before 48 hours, down to the last tick, and nothing changes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(47)]
    public async Task An_administrator_is_refused_before_forty_eight_hours(int hours)
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;
        context.SigningClock.Advance(TimeSpan.FromHours(hours));
        var puts = context.Agent.Puts.Count;

        var result = await context.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningWithdrawalTooEarly", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(puts, context.Agent.Puts.Count);
        Assert.Equal(1, await StoredKeyCountAsync(context));
        Assert.Equal(DnssecState.Withdrawing, (await context.Database.Zones.AsNoTracking().SingleAsync()).DnssecState);
        Assert.False(context.Entries[^1].Succeeded);
        Assert.Equal(DnsAuditActions.ZoneSigningWithdrawn, context.Entries[^1].Action);
    }

    /// <summary>One tick short of the 48 hours is still refused.</summary>
    [Fact]
    public async Task One_tick_short_of_forty_eight_hours_is_still_refused()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;
        context.SigningClock.Advance(TimeSpan.FromHours(48).Subtract(TimeSpan.FromTicks(1)));

        var result = await context.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningWithdrawalTooEarly", result.Error!.Code);
    }

    /// <summary>The zone's owner is refused before 48 hours as well: the rule has no caller in it.</summary>
    [Fact]
    public async Task The_owner_is_refused_before_forty_eight_hours_as_well()
    {
        var (admin, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(DnssecWorld.AccountId), admin.DatabaseName);
        owner.SigningClock.Advance(TimeSpan.FromHours(12));

        var result = await owner.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneSigningWithdrawalTooEarly", result.Error!.Code);
        Assert.Equal(1, await StoredKeyCountAsync(admin));
    }

    /// <summary>Inverse control: at 48 hours the zone becomes unsigned, is published without a key, and its key is deleted.</summary>
    [Fact]
    public async Task After_forty_eight_hours_the_zone_is_unsigned_and_its_key_is_deleted()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;
        context.SigningClock.Advance(TimeSpan.FromHours(48));

        var result = await context.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DnssecState.Off, result.Value.State);
        Assert.Null(result.Value.Ds);
        Assert.Null(result.Value.Dnskey);
        Assert.Null(result.Value.WithdrawalStartedAt);
        Assert.Null(context.Agent.Puts.Last().Signing);
        Assert.Equal(0, await StoredKeyCountAsync(context));
        var zone = await context.Database.Zones.AsNoTracking().SingleAsync();
        Assert.Equal(DnssecState.Off, zone.DnssecState);
        Assert.Null(zone.DnssecWithdrawalStartedAt);
    }

    /// <summary>The owner may end the withdrawal too once the wait has passed.</summary>
    [Fact]
    public async Task The_owner_may_end_the_withdrawal_after_the_wait()
    {
        var (admin, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = admin;
        using var owner = new DnsTestContext(new CustomerCurrentUser(DnssecWorld.AccountId), admin.DatabaseName);
        owner.SigningClock.Advance(TimeSpan.FromHours(72));

        var result = await owner.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await StoredKeyCountAsync(admin));
    }

    /// <summary>The ending moves the serial and is audited with the zone's name and no key.</summary>
    [Fact]
    public async Task Ending_the_withdrawal_moves_the_serial_and_is_audited_without_the_key()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;
        context.SigningClock.Advance(TimeSpan.FromHours(49));
        var before = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;

        await context.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        var after = (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial;
        var entry = context.Entries[^1];
        Assert.True(after > before);
        Assert.Equal(DnsAuditActions.ZoneSigningWithdrawn, entry.Action);
        Assert.Equal(DnssecWorld.ZoneName, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A zone that is signed but not withdrawing, or not signed at all, has no withdrawal to end.</summary>
    [Fact]
    public async Task A_zone_that_is_not_being_withdrawn_has_nothing_to_end()
    {
        var (signed, signedId) = await DnssecWorld.SigningAsync();
        using var _ = signed;
        signed.SigningClock.Advance(TimeSpan.FromDays(10));
        var (off, offId) = await DnssecWorld.OffAsync();
        using var __ = off;

        var fromSigned = await signed.FinishSigningWithdrawal.HandleAsync(Command(signedId), CancellationToken.None);
        var fromOff = await off.FinishSigningWithdrawal.HandleAsync(Command(offId), CancellationToken.None);

        Assert.Equal("ZoneSigningNotWithdrawing", fromSigned.Error!.Code);
        Assert.Equal("ZoneSigningNotWithdrawing", fromOff.Error!.Code);
        Assert.Equal(1, await StoredKeyCountAsync(signed));
    }

    /// <summary>Another account's zone answers not found, and its key stays.</summary>
    [Fact]
    public async Task Another_accounts_zone_answers_not_found_and_keeps_its_key()
    {
        var (admin, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = admin;
        using var intruder = new DnsTestContext(new CustomerCurrentUser(Guid.NewGuid()), admin.DatabaseName);
        intruder.SigningClock.Advance(TimeSpan.FromDays(10));

        var result = await intruder.FinishSigningWithdrawal.HandleAsync(Command(zoneId), CancellationToken.None);

        Assert.Equal("ZoneNotFound", result.Error!.Code);
        Assert.Equal(1, await StoredKeyCountAsync(admin));
    }
}
