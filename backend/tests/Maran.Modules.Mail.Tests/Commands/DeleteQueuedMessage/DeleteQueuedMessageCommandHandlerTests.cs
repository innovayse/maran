using Maran.Modules.Mail.Commands.DeleteQueuedMessage;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Commands.DeleteQueuedMessage;

/// <summary>Behaviour of the handler that removes one queued message.</summary>
public sealed class DeleteQueuedMessageCommandHandlerTests
{
    /// <summary>A queue id that exists is removed through the agent and journaled.</summary>
    [Fact]
    public async Task A_queued_message_is_removed_through_the_agent_and_journaled()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(Command("4A1B2C3D4E5F"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["DeleteQueuedMessage(4A1B2C3D4E5F)"], context.Agent.Calls);
        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailQueueMessageDeleted", entry.Action);
        Assert.Equal("4A1B2C3D4E5F", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The agent's not found becomes the module's own queue-entry code, with the not-found kind.</summary>
    [Fact]
    public async Task The_agents_not_found_becomes_the_queue_entry_not_found_code()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("DeleteQueuedMessage", Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await Handler(context).HandleAsync(Command("4A1B2C3D4E5F"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailServerErrorCodes.MailQueueEntryNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Any other agent failure passes through untouched.</summary>
    [Fact]
    public async Task Any_other_agent_failure_passes_through_untouched()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("DeleteQueuedMessage", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(Command("4A1B2C3D4E5F"), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Builds a command for one queue id.</summary>
    /// <param name="queueId">The queue id.</param>
    /// <returns>The command.</returns>
    private static DeleteQueuedMessageCommand Command(string queueId)
    {
        return new DeleteQueuedMessageCommand(queueId, MailServerCommands.Address, MailServerCommands.Client);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static DeleteQueuedMessageCommandHandler Handler(MailTestContext context)
    {
        return new DeleteQueuedMessageCommandHandler(context.Agent, context.Journal);
    }
}
