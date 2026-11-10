using Maran.Modules.Dns.Commands.DeleteZone;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.DeleteZone;

/// <summary>
/// A zone that is signed or being withdrawn can be deleted, and its key goes with it: deleting a zone must never
/// leave a private key in the database for a zone nobody can see, and an account's deletion removes its signed
/// zones the same way.
/// </summary>
public sealed class DeleteSignedZoneTests
{
    /// <summary>Counts the stored keys through a fresh administrator context.</summary>
    /// <param name="context">The world whose database is read.</param>
    /// <returns>How many keys are stored.</returns>
    private static async Task<int> StoredKeyCountAsync(DnsTestContext context)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), context.DatabaseName);

        return await reader.Database.ZoneSigningKeys.AsNoTracking().CountAsync();
    }

    /// <summary>Deleting a signed zone removes the zone from the host and its key from the database.</summary>
    [Fact]
    public async Task Deleting_a_signed_zone_removes_its_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        Assert.Equal(1, await StoredKeyCountAsync(context));

        var deleted = await context.DeleteZone.HandleAsync(
            new DeleteZoneCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Equal([DnssecWorld.ZoneName], context.Agent.Deletes);
        Assert.Equal(0, await StoredKeyCountAsync(context));
    }

    /// <summary>A zone that is being withdrawn is deleted the same way.</summary>
    [Fact]
    public async Task Deleting_a_withdrawing_zone_removes_its_key()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;

        await context.DeleteZone.HandleAsync(new DeleteZoneCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal(0, await StoredKeyCountAsync(context));
    }

    /// <summary>Inverse control: a host that refuses the removal keeps both the zone and the key.</summary>
    [Fact]
    public async Task A_host_that_refuses_the_removal_keeps_the_zone_and_its_key()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;
        context.Agent.FailNext = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var deleted = await context.DeleteZone.HandleAsync(
            new DeleteZoneCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.False(deleted.IsSuccess);
        Assert.Equal(1, await StoredKeyCountAsync(context));
    }

    /// <summary>Deleting an account removes its signed zones and their keys.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_its_signed_zones_and_their_keys()
    {
        var (context, _) = await DnssecWorld.SigningAsync();
        using var __ = context;

        await context.AccountDeleting.HandleAsync(new AccountDeleting(DnssecWorld.AccountId, "erin"), CancellationToken.None);

        Assert.Empty(context.Agent.ServedNames);
        Assert.Equal(0, await StoredKeyCountAsync(context));
    }
}
