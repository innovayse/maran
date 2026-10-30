using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Services;

/// <summary>
/// The count this module hands the Accounts module's resource summary: the entries the agent reports
/// in the account's crontab, and an explicit unavailable answer when the agent does not report.
/// </summary>
public sealed class CronAccountResourceCounterTests
{
    /// <summary>The account the counts are taken for.</summary>
    private static readonly Guid AccountId = Guid.Parse("8b0f6c4e-2a1d-4c3b-9e5f-7a6d8c9b0e1f");

    /// <summary>The entries the agent lists are counted.</summary>
    [Fact]
    public async Task The_entries_the_agent_lists_are_counted()
    {
        var agent = new RecordingAgentCronClient
        {
            ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok([Entry("one"), Entry("two")]),
        };

        var count = await Counter(agent).CountAsync(AccountId, CancellationToken.None);

        Assert.Equal(AccountResourceKind.CronEntries, count.Kind);
        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(2, count.Used);
        Assert.Equal(["alice"], agent.Lists);
    }

    /// <summary>An agent that fails makes the count unavailable rather than zero.</summary>
    [Fact]
    public async Task An_agent_that_fails_makes_the_count_unavailable_rather_than_zero()
    {
        var agent = new RecordingAgentCronClient
        {
            ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Fail(
                Error.Of("AgentUnavailable", ErrorType.Unavailable)),
        };

        var count = await Counter(agent).CountAsync(AccountId, CancellationToken.None);

        Assert.Equal(AccountResourceCountState.Unavailable, count.State);
    }

    /// <summary>An account the directory cannot resolve is unavailable and the agent is not asked.</summary>
    [Fact]
    public async Task An_account_the_directory_cannot_resolve_is_unavailable_and_the_agent_is_not_asked()
    {
        var agent = new RecordingAgentCronClient();

        var count = await Counter(agent).CountAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AccountResourceCountState.Unavailable, count.State);
        Assert.Empty(agent.Lists);
    }

    /// <summary>Builds the counter over one known account and the given agent.</summary>
    /// <param name="agent">The agent double.</param>
    /// <returns>The counter under test.</returns>
    private static CronAccountResourceCounter Counter(RecordingAgentCronClient agent)
    {
        return new CronAccountResourceCounter(
            new StubAccountDirectory(
                new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active)),
            agent,
            new CapturingLogger<CronAccountResourceCounter>());
    }

    /// <summary>Builds one listed crontab entry.</summary>
    /// <param name="entryId">The entry's id.</param>
    /// <returns>The entry.</returns>
    private static AgentCronEntry Entry(string entryId)
    {
        return new AgentCronEntry(entryId, new AgentCronSchedule("0", "3", "*", "*", "*"), "/usr/bin/backup", true);
    }
}
