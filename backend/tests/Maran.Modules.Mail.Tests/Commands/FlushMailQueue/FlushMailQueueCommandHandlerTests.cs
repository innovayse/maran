using Maran.Modules.Mail.Commands.FlushMailQueue;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Commands.FlushMailQueue;

/// <summary>Behaviour of the handler that asks the mail transfer agent to retry every deferred message.</summary>
public sealed class FlushMailQueueCommandHandlerTests
{
    /// <summary>Flushing asks the agent once, answers with the queue size and journals the request.</summary>
    [Fact]
    public async Task Flushing_asks_the_agent_once_answers_with_the_queue_size_and_journals_the_request()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, result.Value.QueuedMessages);
        Assert.Equal(["FlushQueue()"], context.Agent.Calls);
        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailQueueFlushed", entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refusal by the agent is passed through and journaled as a failure.</summary>
    [Fact]
    public async Task A_refusal_by_the_agent_is_passed_through_and_journaled_as_a_failure()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("FlushQueue", Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Builds the command the tests send.</summary>
    /// <returns>The command.</returns>
    private static FlushMailQueueCommand Command()
    {
        return new FlushMailQueueCommand(MailServerCommands.Address, MailServerCommands.Client);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static FlushMailQueueCommandHandler Handler(MailTestContext context)
    {
        return new FlushMailQueueCommandHandler(context.Agent, context.Journal);
    }
}
