using Maran.Modules.Cron.Commands.SetCronEnvironment;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.SetCronEnvironment;

/// <summary>What replacing an account's cron environment sends, and what it is allowed to record.</summary>
public sealed class SetCronEnvironmentCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An account the caller may not see is answered not found before the agent is called.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_answered_not_found_before_the_agent_is_called()
    {
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.EnvironmentWrites);
    }

    /// <summary>The complete set sent is the complete set the agent is given.</summary>
    [Fact]
    public async Task The_complete_set_sent_is_the_complete_set_the_agent_is_given()
    {
        // A replacement, not a merge: what goes to the agent is exactly what the caller sent, so a
        // handler that quietly added or kept anything would be silently editing a customer's crontab.
        var world = new World();

        var result = await world.HandleAsync(
        [
            new CronEnvironmentVariableDto("PATH", "/usr/local/bin:/usr/bin"),
            new CronEnvironmentVariableDto("TZ", "Europe/Yerevan"),
        ]);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.EnvironmentWrites);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal(["PATH", "TZ"], call.Variables.Select(variable =>
        {
            return variable.Name;
        }));
        Assert.Equal(["/usr/local/bin:/usr/bin", "Europe/Yerevan"], call.Variables.Select(variable =>
        {
            return variable.Value;
        }));
    }

    /// <summary>An empty set is passed to the agent as the request to clear that it is.</summary>
    [Fact]
    public async Task An_empty_set_is_passed_to_the_agent_as_the_request_to_clear_that_it_is()
    {
        // The agent honours an empty list by removing every managed assignment. A handler that
        // treated it as "nothing to do" would leave a customer no way back from a preamble they no
        // longer want.
        var world = new World();

        var result = await world.HandleAsync([]);

        Assert.True(result.IsSuccess);
        Assert.Empty(Assert.Single(world.Agent.EnvironmentWrites).Variables);
    }

    /// <summary>The journal records the names that were set and never their values.</summary>
    [Fact]
    public async Task The_journal_records_the_names_that_were_set_and_never_their_values()
    {
        // A name is what an operator needs — "somebody changed DATABASE_URL on this account at
        // 03:12" answers the question a broken job raises. A value is a credential, in a journal
        // that is append-only and never deleted.
        const string Secret = "postgres://user:HUNTER2@db.internal/app";
        var world = new World();

        await world.HandleAsync(
        [
            new CronEnvironmentVariableDto("DATABASE_URL", Secret),
            new CronEnvironmentVariableDto("TZ", "UTC"),
        ]);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEnvironmentChanged, entry.Action);
        Assert.Equal("DATABASE_URL,TZ", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("HUNTER2", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A value reaches no log line even when the agent refuses the change.</summary>
    [Fact]
    public async Task A_value_reaches_no_log_line_even_when_the_agent_refuses_the_change()
    {
        const string Secret = "postgres://user:HUNTER2@db.internal/app";
        var world = new World();
        world.Agent.SetEnvironmentResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync([new CronEnvironmentVariableDto("DATABASE_URL", Secret)]);

        Assert.False(result.IsSuccess);
        Assert.Equal("CronOperationFailed", result.Error!.Code);

        Assert.NotEmpty(world.Logger.Lines);
        Assert.All(world.Logger.Lines, line =>
        {
            Assert.DoesNotContain("HUNTER2", line, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
        });
    }

    /// <summary>A refused environment change is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_environment_change_is_journalled_as_a_failure()
    {
        // Every refusal on this path — including a cross-tenant probe answered AccountNotFound —
        // must land as a failed row. A journal that recorded a refusal as success would tell an
        // operator a change took effect when the crontab never moved.
        var world = new World();
        world.Agent.SetEnvironmentResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEnvironmentChanged, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Clearing every assignment is journalled as the change it is rather than as an empty subject.</summary>
    [Fact]
    public async Task Clearing_every_assignment_is_journalled_as_the_change_it_is_rather_than_as_an_empty_subject()
    {
        var world = new World();

        await world.HandleAsync([]);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEnvironmentChanged, entry.Action);
        Assert.NotEmpty(entry.Subject);
    }

    /// <summary>A very long list of names is truncated rather than overflowing the journals own column.</summary>
    [Fact]
    public async Task A_very_long_list_of_names_is_truncated_rather_than_overflowing_the_journals_own_column()
    {
        // The journal's subject column is bounded. A composed subject past that bound fails the
        // WRITE, which would mean a legitimate change refused by its own audit trail — so the names
        // are cut to fit. A shortened list still names the change; a failed write records nothing.
        var world = new World();
        var many = Enumerable.Range(0, 24)
            .Select(index =>
            {
                return new CronEnvironmentVariableDto($"VERY_LONG_VARIABLE_NAME_NUMBER_{index:D2}", "x");
            })
            .ToList();

        await world.HandleAsync(many);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.True(
            entry.Subject.Length <= 256,
            $"the journal's subject column holds 256 characters and this one is {entry.Subject.Length}");
        Assert.StartsWith("VERY_LONG_VARIABLE_NAME_NUMBER_00", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Everything one environment test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<SetCronEnvironmentCommandHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public SetCronEnvironmentCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Handler = new SetCronEnvironmentCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new CronAuditJournal(Audit, currentUser),
                Logger);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="variables">The complete new set; one ordinary assignment by default.</param>
        /// <param name="accountId">The account to act under; the owner by default.</param>
        public async Task<Result<bool>> HandleAsync(
            IReadOnlyList<CronEnvironmentVariableDto>? variables = null,
            Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new SetCronEnvironmentCommand(
                    accountId ?? AccountId,
                    variables ?? [new CronEnvironmentVariableDto("TZ", "UTC")],
                    "203.0.113.7",
                    "tests"),
                CancellationToken.None);
        }
    }
}
