using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.Commands.CreateZone;

/// <summary>
/// Who may create which zone: the name server must be on, a customer's name must be claimed by their own
/// site, no name may collide with another account's zone, the plan's allowance holds, and the host is only
/// told about a zone the panel is going to record.
/// </summary>
public sealed class CreateZoneCommandHandlerTests
{
    /// <summary>The account the zones are created for.</summary>
    private static readonly Guid AccountId = new("11111111-1111-4111-8111-111111111111");

    /// <summary>Builds a world for an administrator with the server on and an account allowed two zones.</summary>
    /// <returns>The world.</returns>
    private static async Task<DnsTestContext> AdminWorldAsync()
    {
        var context = new DnsTestContext();
        context.Accounts.Add(AccountId, "alice", maxDnsZones: 2);
        await context.EnableDnsServerAsync();

        return context;
    }

    /// <summary>Builds the command for a name.</summary>
    /// <param name="name">The apex.</param>
    /// <returns>The command.</returns>
    private static CreateZoneCommand Create(string name)
    {
        return new CreateZoneCommand(AccountId, name, "203.0.113.1", "tests");
    }

    /// <summary>An administrator creates a zone, the host serves it, the row exists and the journal says so.</summary>
    [Fact]
    public async Task Creating_a_zone_serves_it_records_it_and_journals_it()
    {
        using var context = await AdminWorldAsync();

        var result = await context.CreateZone.HandleAsync(Create("Example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Name);
        Assert.Equal("example.com", Assert.Single(context.Agent.Puts).Name);
        Assert.Equal(["ns1.panel.test", "ns2.panel.test"], context.Agent.Puts[0].Nameservers);
        Assert.Single(context.Database.Zones);
        var entry = Assert.Single(context.Entries);
        Assert.Equal(DnsAuditActions.DnsZoneCreated, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>With the name server off, nothing reaches the host and the refusal is typed.</summary>
    [Fact]
    public async Task A_zone_is_refused_while_the_name_server_is_off()
    {
        using var context = new DnsTestContext();
        context.Accounts.Add(AccountId, "alice", maxDnsZones: 2);

        var result = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        Assert.Equal("DnsNotEnabled", result.Error!.Code);
        Assert.Empty(context.Agent.Puts);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A customer can only create a zone for a name one of their own sites claims.</summary>
    [Fact]
    public async Task A_customer_needs_a_site_that_claims_the_name()
    {
        using var admin = await AdminWorldAsync();
        using var context = new DnsTestContext(new CustomerCurrentUser(AccountId), admin.DatabaseName);
        context.Accounts.Add(AccountId, "alice", maxDnsZones: 2);

        var refused = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);
        context.Sites.Claim(AccountId, "example.com");
        var accepted = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        Assert.Equal("ZoneApexNotClaimed", refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>The panel's own host name cannot become a zone.</summary>
    [Fact]
    public async Task A_name_the_panel_uses_is_refused()
    {
        using var context = await AdminWorldAsync();
        context.Reserved.Reserved.Add("panel.example.com");

        var result = await context.CreateZone.HandleAsync(Create("panel.example.com"), CancellationToken.None);

        Assert.Equal("ZoneNameReserved", result.Error!.Code);
        Assert.Empty(context.Agent.Puts);
    }

    /// <summary>The same name twice is a conflict, and the second never reaches the host.</summary>
    [Fact]
    public async Task The_same_name_twice_is_a_conflict()
    {
        using var context = await AdminWorldAsync();
        await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        var second = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        Assert.Equal("ZoneNameTaken", second.Error!.Code);
        Assert.Single(context.Agent.Puts);
    }

    /// <summary>A name inside or above another account's zone is refused, and the refusal reveals no owner.</summary>
    [Fact]
    public async Task A_zone_overlapping_another_accounts_zone_is_refused()
    {
        using var context = await AdminWorldAsync();
        var other = new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.com", context.Clock.UtcNow);
        context.Database.Zones.Add(other);
        await context.Database.SaveChangesAsync();

        var child = await context.CreateZone.HandleAsync(Create("sub.example.com"), CancellationToken.None);
        var parent = await context.CreateZone.HandleAsync(Create("com"), CancellationToken.None);
        var unrelated = await context.CreateZone.HandleAsync(Create("example.org"), CancellationToken.None);

        Assert.Equal("ZoneOverlapsForeignZone", child.Error!.Code);
        Assert.False(parent.IsSuccess);
        Assert.True(unrelated.IsSuccess);
    }

    /// <summary>An account may nest its own zones.</summary>
    [Fact]
    public async Task An_account_may_nest_its_own_zones()
    {
        using var context = await AdminWorldAsync();
        await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        var child = await context.CreateZone.HandleAsync(Create("sub.example.com"), CancellationToken.None);

        Assert.True(child.IsSuccess);
    }

    /// <summary>The plan's allowance is enforced before the host is touched.</summary>
    [Fact]
    public async Task The_plan_allowance_is_enforced_before_the_host_is_touched()
    {
        using var context = await AdminWorldAsync();
        await context.CreateZone.HandleAsync(Create("one.example"), CancellationToken.None);
        await context.CreateZone.HandleAsync(Create("two.example"), CancellationToken.None);
        var puts = context.Agent.Puts.Count;

        var third = await context.CreateZone.HandleAsync(Create("three.example"), CancellationToken.None);

        Assert.Equal("ZoneLimitReached", third.Error!.Code);
        Assert.Equal(puts, context.Agent.Puts.Count);
    }

    /// <summary>When another creation takes the last slot meanwhile, the zone is taken back off the host.</summary>
    [Fact]
    public async Task A_zone_that_loses_the_last_slot_is_taken_back_from_the_host()
    {
        using var context = await AdminWorldAsync();
        context.SlotGate.RefuseAlways = true;

        var result = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        Assert.Equal("ZoneLimitReachedConcurrently", result.Error!.Code);
        Assert.Equal("example.com", Assert.Single(context.Agent.Deletes));
        Assert.Empty(context.Agent.ServedNames);
        Assert.Empty(context.Database.Zones);
    }

    /// <summary>
    /// The name's lock is held before the host is told about the zone, so the start-up pass cannot take the
    /// zone off the host between its arrival and its row.
    /// </summary>
    [Fact]
    public async Task The_name_is_locked_before_the_host_is_touched()
    {
        using var context = await AdminWorldAsync();
        var putsWhenLocked = -1;
        context.ZoneLock.OnNameGranted = _ =>
        {
            putsWhenLocked = context.Agent.Puts.Count;
            return Task.CompletedTask;
        };

        var result = await context.CreateZone.HandleAsync(Create("Example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["example.com"], context.ZoneLock.NamesLocked);
        Assert.Equal(0, putsWhenLocked);
    }

    /// <summary>A host that refuses the zone leaves no row.</summary>
    [Fact]
    public async Task A_host_refusal_leaves_no_row()
    {
        using var context = await AdminWorldAsync();
        context.Agent.FailNext = Error.Of("AgentValidationFailed", ErrorType.Validation);

        var result = await context.CreateZone.HandleAsync(Create("example.com"), CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Empty(context.Database.Zones);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An account the caller cannot see is "not found", never "forbidden".</summary>
    [Fact]
    public async Task An_unknown_account_is_not_found()
    {
        using var context = await AdminWorldAsync();

        var result = await context.CreateZone.HandleAsync(
            new CreateZoneCommand(Guid.NewGuid(), "example.com", "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
