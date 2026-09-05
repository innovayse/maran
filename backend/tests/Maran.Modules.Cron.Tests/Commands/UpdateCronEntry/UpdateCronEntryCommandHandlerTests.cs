using Maran.Modules.Cron.Commands.UpdateCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.UpdateCronEntry;

/// <summary>What rewriting a cron entry changes, what it refuses, and what it records.</summary>
public sealed class UpdateCronEntryCommandHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An entry under an account the caller may not see is answered not found and never forbidden.</summary>
    [Fact]
    public async Task An_entry_under_an_account_the_caller_may_not_see_is_answered_not_found_and_never_forbidden()
    {
        // The IDOR case for this operation. There are no rows here and therefore no query filter:
        // the account resolution is the boundary, and it answers null for somebody else's account,
        // which becomes a 404 — never a 403, which would confirm the entry exists.
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Updates);
    }

    /// <summary>An entry the crontab does not hold is answered not found.</summary>
    [Fact]
    public async Task An_entry_the_crontab_does_not_hold_is_answered_not_found()
    {
        // The same answer another tenant's entry would get if it could be reached at all, which is
        // what makes the pair indistinguishable to a caller probing for entry ids.
        var world = new World();
        world.Agent.UpdateEntryResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryNotFound", result.Error!.Code);
    }

    /// <summary>An update rewrites the entry and never touches its enablement.</summary>
    [Fact]
    public async Task An_update_rewrites_the_entry_and_never_touches_its_enablement()
    {
        // The two are separate calls on purpose. An update that also carried the flag would switch a
        // disabled entry back on whenever a customer edited its command — a job that starts running
        // again with nobody having asked.
        var world = new World();

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.Updates);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal(EntryId, call.EntryId);
        Assert.Empty(world.Agent.EnabledChanges);
    }

    /// <summary>An update is journalled with the entry id and never with the command.</summary>
    [Fact]
    public async Task An_update_is_journalled_with_the_entry_id_and_never_with_the_command()
    {
        var world = new World();

        await world.HandleAsync(command: "/usr/bin/curl https://example.test/hook?token=SECRETTOKEN");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryUpdated, entry.Action);
        Assert.Equal(EntryId, entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("SECRETTOKEN", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused update is journalled as a failure and leaves the command out of every log line.</summary>
    [Fact]
    public async Task A_refused_update_is_journalled_as_a_failure_and_leaves_the_command_out_of_every_log_line()
    {
        const string Secret = "SECRETTOKENVALUE";
        var world = new World();
        world.Agent.UpdateEntryResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        await world.HandleAsync(command: $"/usr/bin/curl https://example.test/hook?token={Secret}");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryUpdated, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(EntryId, entry.Subject);

        Assert.NotEmpty(world.Logger.Lines);
        Assert.All(world.Logger.Lines, line =>
        {
            Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
        });
    }

    /// <summary>Everything one update test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<UpdateCronEntryCommandHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public UpdateCronEntryCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Handler = new UpdateCronEntryCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new CronAuditJournal(Audit, currentUser),
                Logger);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="command">The new command line.</param>
        /// <param name="accountId">The account to act under; the owner by default.</param>
        public async Task<Result<bool>> HandleAsync(string command = "/usr/bin/backup", Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new UpdateCronEntryCommand(
                    accountId ?? AccountId,
                    EntryId,
                    new CronScheduleDto("0", "4", "*", "*", "*"),
                    command,
                    "203.0.113.7",
                    "tests"),
                CancellationToken.None);
        }
    }
}
