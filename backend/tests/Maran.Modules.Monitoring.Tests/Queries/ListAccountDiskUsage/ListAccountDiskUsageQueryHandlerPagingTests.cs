using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;
using Maran.Modules.Monitoring.Resources;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.ListAccountDiskUsage;

/// <summary>
/// The paging half of the host disk view: the page is chosen from the panel's accounts, and the agent
/// is asked about that page and nothing else — because a measurement is a walk of a home directory.
/// </summary>
public sealed class ListAccountDiskUsageQueryHandlerPagingTests
{
    /// <summary>The agent is asked about exactly the user names on the page, and no other.</summary>
    /// <remarks>
    /// The whole reason the list is paged where the data lives: asking about every account to show ten
    /// of them is a walk of every home on the host, on every refresh.
    /// </remarks>
    [Fact]
    public async Task The_agent_is_asked_about_exactly_the_user_names_on_the_page()
    {
        var agent = Agent();
        var handler = Handler(agent, Accounts("alice", "bob", "carol", "dave"));

        await handler.HandleAsync(new ListAccountDiskUsageQuery(Page: 2, PageSize: 10), CancellationToken.None);
        await handler.HandleAsync(new ListAccountDiskUsageQuery(PageSize: 10), CancellationToken.None);

        // Page 2 of four accounts at ten a page is empty and asks nothing; page 1 asks for all four.
        var asked = Assert.Single(agent.DiskUsageRequests);
        Assert.Equal(["alice", "bob", "carol", "dave"], asked);
    }

    /// <summary>A page past the first asks only for that page's names, and carries the true total.</summary>
    [Fact]
    public async Task A_later_page_asks_only_for_its_own_names_and_carries_the_true_total()
    {
        var names = Enumerable.Range(1, 12)
            .Select(index =>
            {
                return $"user{index:D2}";
            })
            .ToArray();
        var agent = Agent();
        var handler = Handler(agent, Accounts(names));

        var result = await handler.HandleAsync(
            new ListAccountDiskUsageQuery(Page: 2, PageSize: 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["user11", "user12"], Usernames(result.Value));
        Assert.Equal(["user11", "user12"], Assert.Single(agent.DiskUsageRequests));
    }

    /// <summary>
    /// An empty page never calls the agent, because an empty name list on the wire is a walk of every
    /// home on the host.
    /// </summary>
    [Fact]
    public async Task An_empty_page_never_calls_the_agent()
    {
        var agent = Agent();
        var handler = Handler(agent, Accounts("alice"));

        var result = await handler.HandleAsync(
            new ListAccountDiskUsageQuery(Search: SearchTerm.Of("nobody")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
        Assert.Empty(agent.DiskUsageRequests);
        Assert.Equal(0, agent.QuotaEnforceabilityCalls);
    }

    /// <summary>A search keeps the accounts whose user name contains the term, ignoring case.</summary>
    [Fact]
    public async Task A_search_keeps_the_accounts_whose_user_name_contains_the_term()
    {
        var agent = Agent();
        var handler = Handler(agent, Accounts("alice", "malina", "bob"));

        var result = await handler.HandleAsync(
            new ListAccountDiskUsageQuery(Search: SearchTerm.Of("ALI")),
            CancellationToken.None);

        Assert.Equal(2, result.Value.Total);
        Assert.Equal(["alice", "malina"], Usernames(result.Value));
        Assert.Equal(["alice", "malina"], Assert.Single(agent.DiskUsageRequests));
    }

    /// <summary>A descending user-name sort reverses the page.</summary>
    [Fact]
    public async Task A_descending_user_name_sort_reverses_the_page()
    {
        var handler = Handler(Agent(), Accounts("alice", "carol", "bob"));

        var result = await handler.HandleAsync(
            new ListAccountDiskUsageQuery(Sort: SortRequest.Parse("-username")),
            CancellationToken.None);

        Assert.Equal(["carol", "bob", "alice"], Usernames(result.Value));
    }

    /// <summary>The quota sort orders by the plan's allowance, which needs no measurement.</summary>
    [Fact]
    public async Task The_quota_sort_orders_by_the_plans_allowance()
    {
        var handler = Handler(
            Agent(),
            new StubAccountDirectory(
                Snapshot("alice", diskQuotaMb: 2_048),
                Snapshot("bob", diskQuotaMb: 512),
                Snapshot("carol", diskQuotaMb: 1_024)));

        var result = await handler.HandleAsync(
            new ListAccountDiskUsageQuery(Sort: SortRequest.Parse("quotaBytes")),
            CancellationToken.None);

        Assert.Equal(["bob", "carol", "alice"], Usernames(result.Value));
    }

    /// <summary>A page account the agent did not report is unmeasured rather than zero.</summary>
    [Fact]
    public async Task A_page_account_the_agent_did_not_report_is_unmeasured_rather_than_zero()
    {
        var agent = Agent(new AgentAccountDiskUsage("alice", 42));
        var handler = Handler(agent, Accounts("alice", "bob"));

        var result = await handler.HandleAsync(new ListAccountDiskUsageQuery(), CancellationToken.None);

        Assert.Equal(42L, result.Value.Items[0].UsedBytes);
        Assert.Null(result.Value.Items[1].UsedBytes);
    }

    /// <summary>Builds an agent that measured the given rows and can enforce quotas.</summary>
    /// <param name="measured">What the agent reports.</param>
    /// <returns>The stub agent.</returns>
    private static StubAgentMonitorClient Agent(params AgentAccountDiskUsage[] measured)
    {
        return new StubAgentMonitorClient
        {
            DiskUsage = Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok(measured),
            QuotaEnforceability = Result<AgentQuotaEnforceability>.Ok(
                new AgentQuotaEnforceability(true, QuotaUnenforceableReason.Unspecified)),
        };
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="agent">The stub agent.</param>
    /// <param name="accounts">The panel's accounts.</param>
    /// <returns>The handler.</returns>
    private static ListAccountDiskUsageQueryHandler Handler(
        StubAgentMonitorClient agent,
        StubAccountDirectory accounts)
    {
        return new ListAccountDiskUsageQueryHandler(agent, accounts, new StubStringLocalizer<DisplayNames>());
    }

    /// <summary>A directory of accounts with the given names, each on the same plan.</summary>
    /// <param name="usernames">The accounts' system user names.</param>
    /// <returns>The directory.</returns>
    private static StubAccountDirectory Accounts(params string[] usernames)
    {
        return new StubAccountDirectory(usernames
            .Select(username =>
            {
                return Snapshot(username, diskQuotaMb: 512);
            })
            .ToArray());
    }

    /// <summary>The user names of a page's rows, in page order.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The user names.</returns>
    private static IEnumerable<string> Usernames(PagedResult<AccountDiskUsageDto> page)
    {
        return page.Items.Select(row =>
        {
            return row.Username;
        });
    }

    /// <summary>Builds one account snapshot, naming only the fields these tests care about.</summary>
    /// <param name="username">Its system user name.</param>
    /// <param name="diskQuotaMb">The plan's disk allowance, in megabytes.</param>
    /// <returns>The snapshot.</returns>
    private static AccountSnapshot Snapshot(string username, int diskQuotaMb)
    {
        return new AccountSnapshot(
            Guid.NewGuid(),
            username,
            MaxSites: 5,
            MaxDatabases: 5,
            MaxSftpUsers: 5,
            MaxCronEntries: 5,
            MaxPhpWorkersPerPool: 5,
            DiskQuotaMb: diskQuotaMb,
            State: AccountState.Active);
    }
}
