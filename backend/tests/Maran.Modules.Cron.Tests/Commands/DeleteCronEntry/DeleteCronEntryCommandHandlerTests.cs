using Maran.Modules.Cron.Commands.DeleteCronEntry;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.DeleteCronEntry;

/// <summary>What removing a cron entry does, what it refuses, and what it records.</summary>
public sealed class DeleteCronEntryCommandHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An entry under an account the caller may not see is answered not found and never forbidden.</summary>
    [Fact]
    public async Task An_entry_under_an_account_the_caller_may_not_see_is_answered_not_found_and_never_forbidden()
    {
        // The IDOR case for the most destructive operation in the module: a removal aimed at another
        // tenant must not reach the agent at all, and must read as "no such thing" rather than as
        // "yours to ask about, but not yours".
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Deletes);
    }

    /// <summary>A removal addresses the agent by the accounts system user name and the entry id.</summary>
    [Fact]
    public async Task A_removal_addresses_the_agent_by_the_accounts_system_user_name_and_the_entry_id()
    {
        var world = new World();

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.Deletes);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal(EntryId, call.EntryId);
    }

    /// <summary>A second removal of the same entry is answered not found.</summary>
    [Fact]
    public async Task A_second_removal_of_the_same_entry_is_answered_not_found()
    {
        var world = new World();
        world.Agent.DeleteEntryResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryNotFound", result.Error!.Code);
    }

    /// <summary>A removal is journalled with the entry id.</summary>
    [Fact]
    public async Task A_removal_is_journalled_with_the_entry_id()
    {
        // The id is all that survives a removal, which is exactly why it is the subject: the entry it
        // named is gone from the crontab, and the journal row is the only remaining record that it
        // ever existed.
        var world = new World();

        await world.HandleAsync();

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryDeleted, entry.Action);
        Assert.Equal(EntryId, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused removal is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_removal_is_journalled_as_a_failure()
    {
        var world = new World();
        world.Agent.DeleteEntryResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        await world.HandleAsync();

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryDeleted, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(EntryId, entry.Subject);
    }

    /// <summary>Everything one removal test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<DeleteCronEntryCommandHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public DeleteCronEntryCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Handler = new DeleteCronEntryCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new CronAuditJournal(Audit, currentUser),
                Logger);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="accountId">The account to act under; the owner by default.</param>
        public async Task<Result<bool>> HandleAsync(Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new DeleteCronEntryCommand(accountId ?? AccountId, EntryId, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
