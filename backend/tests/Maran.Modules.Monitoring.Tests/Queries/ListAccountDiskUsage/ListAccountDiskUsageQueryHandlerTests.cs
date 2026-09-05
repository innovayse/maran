using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.ListAccountDiskUsage;

/// <summary>
/// The join at the heart of the host disk view: what the agent measured on the filesystem, beside
/// what the panel's plan allows — two sources that share nothing but a system user name.
/// </summary>
public sealed class ListAccountDiskUsageQueryHandlerTests
{
    /// <summary>A measured account carries the agent's own byte count, not a figure invented here.</summary>
    /// <remarks>
    /// The whole point of the widening: a per-account disk view that is plumbed through but never
    /// populated passes every other test in the suite. This asserts the exact number the agent
    /// reported arrives on the matching account's row.
    /// </remarks>
    [Fact]
    public async Task A_measured_account_carries_the_agents_own_byte_count()
    {
        var accountId = Guid.NewGuid();
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("alice", 734_003_200)],
            known: [Snapshot(accountId, "alice", diskQuotaMb: 1_024)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal(accountId, row.AccountId);
        Assert.Equal("alice", row.Username);
        Assert.Equal(734_003_200L, row.UsedBytes);
    }

    /// <summary>The quota is the plan's megabytes expressed in the bytes the used figure is in.</summary>
    /// <remarks>
    /// The two sides of the row must share a unit or the comparison is nonsense: 1,024 MB is
    /// 1,073,741,824 bytes, and a row pairing "1024" with "734003200" would draw an account at
    /// seventy-one million percent of its allowance.
    /// </remarks>
    [Fact]
    public async Task The_quota_is_the_plans_megabytes_expressed_in_bytes()
    {
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("alice", 1)],
            known: [Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 1_024)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal(1_073_741_824L, row.QuotaBytes);
    }

    /// <summary>A quota above two gigabytes does not overflow into a negative allowance.</summary>
    /// <remarks>
    /// The allowance is stored as an <c>int</c> count of megabytes, so multiplying it by an
    /// <c>int</c> 1,048,576 wraps above roughly 2,047 MB. Every plan a hosting company actually sells
    /// is above that, and the wrapped figure is NEGATIVE — which reads as an account far over a quota
    /// it is nowhere near.
    /// </remarks>
    [Fact]
    public async Task A_quota_above_two_gigabytes_does_not_overflow_into_a_negative_allowance()
    {
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("alice", 1)],
            known: [Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 51_200)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal(53_687_091_200L, row.QuotaBytes);
    }

    /// <summary>An account the agent did not measure has no figure rather than a zero.</summary>
    /// <remarks>
    /// A zero is a claim — "this account holds nothing" — and it is a different claim from "nobody
    /// has measured this account". An account created seconds ago, or one whose home the agent could
    /// not walk, is the second; rendering it as the first tells an operator something untrue about a
    /// customer.
    /// </remarks>
    [Fact]
    public async Task An_account_the_agent_did_not_measure_has_no_figure_rather_than_a_zero()
    {
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("alice", 1_000)],
            known: [Snapshot(Guid.NewGuid(), "bob", diskQuotaMb: 512)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("bob", row.Username);
        Assert.Null(row.UsedBytes);
    }

    /// <summary>An account the agent reports and the panel does not know is left out of the listing.</summary>
    /// <remarks>
    /// The agent reads the host's password database, so a row it reports that the panel has never
    /// heard of is a system user whose name happens to parse — not a customer. Billing it to nobody
    /// in a customer listing would be inventing a tenant.
    /// </remarks>
    [Fact]
    public async Task An_account_the_agent_reports_and_the_panel_does_not_know_is_left_out()
    {
        var handler = HandlerFor(
            measured:
            [
                new AgentAccountDiskUsage("alice", 10),
                new AgentAccountDiskUsage("backup", 999_999),
            ],
            known: [Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 512)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("alice", row.Username);
    }

    /// <summary>Two user names differing only in case are two users, not one.</summary>
    /// <remarks>
    /// A Linux user name is case-sensitive, so a culture-aware or case-insensitive match would bill
    /// one account for another's bytes. The agent measured <c>Alice</c>; the panel's account is
    /// <c>alice</c>, and it has not been measured.
    /// </remarks>
    [Fact]
    public async Task Two_user_names_differing_only_in_case_are_two_users()
    {
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("Alice", 4_096)],
            known: [Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 512)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Null(row.UsedBytes);
    }

    /// <summary>A byte count above the signed range saturates rather than turning negative.</summary>
    /// <remarks>
    /// The agent reports an unsigned 64-bit count and the panel carries a signed one, so an unchecked
    /// cast of anything above <c>long.MaxValue</c> produces an account using less than nothing.
    /// </remarks>
    [Fact]
    public async Task A_byte_count_above_the_signed_range_saturates_rather_than_turning_negative()
    {
        var handler = HandlerFor(
            measured: [new AgentAccountDiskUsage("alice", ulong.MaxValue)],
            known: [Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 512)]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal(long.MaxValue, row.UsedBytes);
    }

    /// <summary>The agent's refusal is returned and no listing is invented from the panel's side alone.</summary>
    /// <remarks>
    /// Without a measurement there is nothing to show but a column of allowances, and answering with
    /// that would read as a host full of accounts using nothing.
    /// </remarks>
    [Fact]
    public async Task The_agents_refusal_is_returned_and_no_listing_is_invented()
    {
        var accounts = new StubAccountDirectory(Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 512));
        var agent = new StubAgentMonitorClient
        {
            DiskUsage = Result<IReadOnlyList<AgentAccountDiskUsage>>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)),
        };
        var handler = new ListAccountDiskUsageQueryHandler(agent, accounts);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(0, accounts.Listings);
    }

    /// <summary>Accounts come back ordered by user name, so two reads of one host agree.</summary>
    [Fact]
    public async Task Accounts_come_back_ordered_by_user_name()
    {
        var handler = HandlerFor(
            measured: [],
            known:
            [
                Snapshot(Guid.NewGuid(), "carol", diskQuotaMb: 512),
                Snapshot(Guid.NewGuid(), "alice", diskQuotaMb: 512),
                Snapshot(Guid.NewGuid(), "bob", diskQuotaMb: 512),
            ]);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        Assert.Equal(
            ["alice", "bob", "carol"],
            result.Value.Select(row =>
            {
                return row.Username;
            }));
    }

    /// <summary>A host with no accounts answers an empty listing, not a failure.</summary>
    [Fact]
    public async Task A_host_with_no_accounts_answers_an_empty_listing()
    {
        var handler = HandlerFor(measured: [new AgentAccountDiskUsage("root", 1)], known: []);

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>Builds the handler over an agent that measured one thing and a panel that knows another.</summary>
    /// <param name="measured">What the agent reports.</param>
    /// <param name="known">The accounts the panel knows about.</param>
    /// <returns>The handler under test.</returns>
    private static ListAccountDiskUsageQueryHandler HandlerFor(
        AgentAccountDiskUsage[] measured,
        AccountSnapshot[] known)
    {
        var agent = new StubAgentMonitorClient
        {
            DiskUsage = Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok(measured),
        };

        return new ListAccountDiskUsageQueryHandler(agent, new StubAccountDirectory(known));
    }

    /// <summary>Builds one account snapshot, naming only the fields these tests care about.</summary>
    /// <param name="id">The account's identity.</param>
    /// <param name="username">Its system user name — the only identifier the agent shares.</param>
    /// <param name="diskQuotaMb">The plan's disk allowance, in megabytes.</param>
    /// <returns>The snapshot.</returns>
    private static AccountSnapshot Snapshot(Guid id, string username, int diskQuotaMb)
    {
        return new AccountSnapshot(
            id,
            username,
            MaxSites: 5,
            MaxDatabases: 5,
            MaxSftpUsers: 5,
            MaxCronEntries: 5,
            MaxPhpWorkersPerPool: 5,
            DiskQuotaMb: diskQuotaMb);
    }
}
