using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailQueue;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.ListMailQueue;

/// <summary>Behaviour of the query that lists the host's mail queue.</summary>
public sealed class ListMailQueueQueryHandlerTests
{
    /// <summary>The agent's listing is carried with its total and its truncation flag.</summary>
    [Fact]
    public async Task The_agents_listing_is_carried_with_its_total_and_its_truncation_flag()
    {
        using var context = new MailTestContext();
        var arrived = new DateTimeOffset(2026, 11, 7, 8, 30, 0, TimeSpan.Zero);
        context.Agent.Queue = new AgentMailQueueListing(
            [
                new AgentMailQueueEntry(
                    "4A1B2C3D4E5F",
                    AgentMailQueueName.Deferred,
                    "bounce@example.net",
                    3,
                    2048,
                    arrived,
                    AgentMailDeferralClass.RemoteRefused),
            ],
            500,
            true);

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(500u, result.Value.Total);
        Assert.True(result.Value.Truncated);
        var row = Assert.Single(result.Value.Entries);
        Assert.Equal("4A1B2C3D4E5F", row.QueueId);
        Assert.Equal(MailQueueName.Deferred, row.Queue);
        Assert.Equal("bounce@example.net", row.Sender);
        Assert.Equal(3u, row.RecipientCount);
        Assert.Equal(2048ul, row.SizeBytes);
        Assert.Equal(arrived, row.ArrivedAt);
        Assert.Equal(MailDeferralClass.RemoteRefused, row.DeferralClass);
    }

    /// <summary>The agent is asked for its own default limit.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_its_own_default_limit()
    {
        using var context = new MailTestContext();

        await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.Equal(["ListQueue(limit=0)"], context.Agent.Calls);
    }

    /// <summary>An empty queue is an empty list, not a failure.</summary>
    [Fact]
    public async Task An_empty_queue_is_an_empty_list()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Entries);
        Assert.Equal(0u, result.Value.Total);
        Assert.False(result.Value.Truncated);
    }

    /// <summary>A failure of the agent is passed through.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_is_passed_through()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("ListQueue", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static ListMailQueueQueryHandler Handler(MailTestContext context)
    {
        return new ListMailQueueQueryHandler(context.Agent);
    }
}
