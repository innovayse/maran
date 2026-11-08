using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.IntegrationEvents;

/// <summary>Deleting an account takes its zones off the name server and out of the database, or stops the deletion.</summary>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>The account being deleted.</summary>
    private static readonly Guid AccountId = new("33333333-3333-4333-8333-333333333333");

    /// <summary>Builds a world with two zones of the account.</summary>
    /// <returns>The world.</returns>
    private static async Task<DnsTestContext> WorldAsync()
    {
        var context = new DnsTestContext();
        context.Accounts.Add(AccountId, "carol", maxDnsZones: 5);
        await context.EnableDnsServerAsync();
        await context.CreateZone.HandleAsync(new CreateZoneCommand(AccountId, "one.example", "203.0.113.1", "t"), CancellationToken.None);
        await context.CreateZone.HandleAsync(new CreateZoneCommand(AccountId, "two.example", "203.0.113.1", "t"), CancellationToken.None);

        return context;
    }

    /// <summary>Both zones leave the host and the database.</summary>
    [Fact]
    public async Task The_zones_leave_the_host_and_the_database()
    {
        using var context = await WorldAsync();

        await context.AccountDeleting.HandleAsync(new AccountDeleting(AccountId, "carol"), CancellationToken.None);

        Assert.Empty(context.Agent.ServedNames);
        Assert.Empty(context.Database.Zones);
    }

    /// <summary>A host that refuses a removal stops the deletion and keeps the rows.</summary>
    [Fact]
    public async Task A_host_refusal_stops_the_deletion()
    {
        using var context = await WorldAsync();
        context.Agent.FailNext = Error.Of("AgentSystemFailure", ErrorType.Failure);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await context.AccountDeleting.HandleAsync(new AccountDeleting(AccountId, "carol"), CancellationToken.None);
        });

        Assert.Equal(2, context.Database.Zones.Count());
    }

    /// <summary>An account with no zone never reaches the agent.</summary>
    [Fact]
    public async Task An_account_without_zones_never_reaches_the_agent()
    {
        using var context = new DnsTestContext();
        context.Agent.FailNext = Error.Of("AgentSystemFailure", ErrorType.Failure);

        await context.AccountDeleting.HandleAsync(new AccountDeleting(Guid.NewGuid(), "nobody"), CancellationToken.None);

        Assert.NotNull(context.Agent.FailNext);
    }
}
