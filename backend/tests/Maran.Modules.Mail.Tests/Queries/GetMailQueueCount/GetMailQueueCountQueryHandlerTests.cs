using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Queries.GetMailQueueCount;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.GetMailQueueCount;

/// <summary>Behaviour of the query that counts the host's mail queue for the navigation badge.</summary>
public sealed class GetMailQueueCountQueryHandlerTests
{
    /// <summary>The queue's total passes through, not the number of entries asked for.</summary>
    [Fact]
    public async Task The_queues_total_passes_through_and_not_the_number_of_entries_asked_for()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.Queue = new AgentMailQueueListing([], 37, true);

        var result = await Handler(context).HandleAsync(new GetMailQueueCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(37u, result.Value.Count);
    }

    /// <summary>The agent is asked for one entry, so the cost does not grow with the queue.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_one_entry_so_the_cost_does_not_grow_with_the_queue()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        await Handler(context).HandleAsync(new GetMailQueueCountQuery(), CancellationToken.None);

        Assert.Equal(["ListQueue(limit=1)"], context.Agent.Calls);
    }

    /// <summary>A queue that cannot be read is the agent's failure and never a count of zero.</summary>
    [Fact]
    public async Task A_queue_that_cannot_be_read_is_the_agents_failure_and_never_a_count_of_zero()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.FailNext("ListQueue", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(new GetMailQueueCountQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>A server mail was never turned on for holds no queue of ours, so it counts zero without asking the agent.</summary>
    [Fact]
    public async Task A_server_mail_was_never_turned_on_for_counts_zero_without_asking_the_agent()
    {
        using var context = new MailTestContext();
        context.Agent.Queue = new AgentMailQueueListing([], 12, false);

        var result = await Handler(context).HandleAsync(new GetMailQueueCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, result.Value.Count);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static GetMailQueueCountQueryHandler Handler(MailTestContext context)
    {
        return new GetMailQueueCountQueryHandler(context.Agent, context.Server);
    }
}
