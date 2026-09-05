using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Commands.CreateCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.CreateCronEntry;

/// <summary>
/// What installing a cron entry refuses, what it counts the plan limit against, and what it is
/// forbidden to write down.
/// </summary>
public sealed class CreateCronEntryCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An entry beyond the plans allowance is refused before the agent installs anything.</summary>
    [Fact]
    public async Task An_entry_beyond_the_plans_allowance_is_refused_before_the_agent_installs_anything()
    {
        var world = new World(maxCronEntries: 1);
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok([Installed("aaaa1111-0000-4000-8000-000000000001")]);

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryLimitReached", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>The plan limit is counted against the crontab the agent reports and not against a panel table.</summary>
    [Fact]
    public async Task The_plan_limit_is_counted_against_the_crontab_the_agent_reports_and_not_against_a_panel_table()
    {
        // This module keeps no rows, so there is nothing local to count — and that is the point
        // rather than a limitation. The two entries below were never installed through the panel:
        // the account wrote them into its own crontab over SFTP, which it is free to do. A limit
        // counted against what the panel had installed would read this account as empty and let the
        // creation through, so a customer could pass their allowance simply by editing their crontab.
        var world = new World(maxCronEntries: 2);
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok(
        [
            Installed("aaaa1111-0000-4000-8000-000000000001"),
            Installed("aaaa1111-0000-4000-8000-000000000002"),
        ]);

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryLimitReached", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>An entry inside the plans allowance is installed.</summary>
    [Fact]
    public async Task An_entry_inside_the_plans_allowance_is_installed()
    {
        // The boundary from the other side, so the limit cannot pass by refusing everything: a guard
        // that says no to every creation is not a guard, it is an outage.
        var world = new World(maxCronEntries: 2);
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Ok([Installed("aaaa1111-0000-4000-8000-000000000001")]);

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Creates);
    }

    /// <summary>A listing the agent refuses is not read as an empty crontab.</summary>
    [Fact]
    public async Task A_listing_the_agent_refuses_is_not_read_as_an_empty_crontab()
    {
        // The failure mode this ordering exists to avoid: if a refused listing were treated as "no
        // entries yet", an agent outage would turn every plan into an unlimited one, and the entries
        // installed during it would be over the limit forever afterwards.
        var world = new World(maxCronEntries: 1);
        world.Agent.ListEntriesResult = Result<IReadOnlyList<AgentCronEntry>>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronOperationFailed", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>An account the caller may not see is answered not found before the agent is called.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_answered_not_found_before_the_agent_is_called()
    {
        // The whole tenant boundary of this module, in one assertion: it keeps no rows, so nothing
        // else stands between a guessed account id and another customer's crontab. 404, never 403.
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Lists);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A duplicate the agent already holds is answered as a conflict and not as a generic failure.</summary>
    [Fact]
    public async Task A_duplicate_the_agent_already_holds_is_answered_as_a_conflict_and_not_as_a_generic_failure()
    {
        // The agent answers AlreadyExists rather than installing a second identical line, and the
        // panel must carry that distinction: the code ends in AlreadyExists, which the result
        // translation turns into a 409, so a customer is told their job is already scheduled instead
        // of being told something went wrong and retrying.
        var world = new World();
        world.Agent.CreateEntryResult = Result<string>.Fail(Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryAlreadyExists", result.Error!.Code);
    }

    /// <summary>An agent failure is answered with this modules own code and never with the agents.</summary>
    [Fact]
    public async Task An_agent_failure_is_answered_with_this_modules_own_code_and_never_with_the_agents()
    {
        // RULING 31: a cron rpc's failure is re-coded rather than forwarded, so the sentence a
        // customer reads is one this module owns and translates — never one built around an agent
        // diagnostic that could quote the command back.
        var world = new World();
        world.Agent.CreateEntryResult = Result<string>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronOperationFailed", result.Error!.Code);
        Assert.DoesNotContain("Agent", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>The agent is addressed by the accounts system user name and never by an identifier the caller sent.</summary>
    [Fact]
    public async Task The_agent_is_addressed_by_the_accounts_system_user_name_and_never_by_an_identifier_the_caller_sent()
    {
        // The account id is resolved to a user name INSIDE the handler, through the tenant-scoped
        // directory. A request that could name the crontab directly would be a request that can name
        // another tenant's.
        var world = new World();

        await world.HandleAsync();

        Assert.Equal("alice", Assert.Single(world.Agent.Lists));
        Assert.Equal("alice", Assert.Single(world.Agent.Creates).AccountUsername);
    }

    /// <summary>The schedule reaches the agent field for field.</summary>
    [Fact]
    public async Task The_schedule_reaches_the_agent_field_for_field()
    {
        // Five strings, two of them one-based: a transposed pair compiles, passes every type check
        // and installs a job that runs at another time. This is the assertion that would notice.
        var world = new World();

        await world.HandleAsync(schedule: new CronScheduleDto("5", "4", "3", "2", "1"));

        var call = Assert.Single(world.Agent.Creates);
        Assert.Equal(new AgentCronSchedule("5", "4", "3", "2", "1"), call.Schedule);
    }

    /// <summary>A created entry is reported with the identifier the agent minted and as enabled.</summary>
    [Fact]
    public async Task A_created_entry_is_reported_with_the_identifier_the_agent_minted_and_as_enabled()
    {
        // The id comes from the agent's answer and is never invented here: it is the only handle a
        // later update, deletion or output read has, so a panel-side guess would address nothing.
        var world = new World();

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(RecordingAgentCronClient.MintedEntryId, result.Value.EntryId);
        Assert.True(result.Value.Enabled);
        Assert.Equal(AccountId, result.Value.AccountId);
    }

    /// <summary>A creation is journalled with the entry id and never with the command.</summary>
    [Fact]
    public async Task A_creation_is_journalled_with_the_entry_id_and_never_with_the_command()
    {
        var world = new World();

        await world.HandleAsync(command: "/usr/bin/mysql -pHUNTER2SECRET");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryCreated, entry.Action);
        Assert.Equal(RecordingAgentCronClient.MintedEntryId, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused creation is journalled as a failure against the account.</summary>
    [Fact]
    public async Task A_refused_creation_is_journalled_as_a_failure_against_the_account()
    {
        // Failures are the half of the journal worth reading, and a plan limit hit is one an
        // operator is asked about. The subject is the ACCOUNT because no entry exists yet — there is
        // no id to name, and the command may not be named.
        var world = new World(maxCronEntries: 0);

        await world.HandleAsync();

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryCreated, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(AccountId.ToString(), entry.Subject);
    }

    /// <summary>A command carrying a password reaches no log line and no audit row.</summary>
    [Fact]
    public async Task A_command_carrying_a_password_reaches_no_log_line_and_no_audit_row()
    {
        // RULING 31, asserted rather than promised. A cron command legitimately carries credentials —
        // `mysql -pSECRET`, a curl with a token — and both the journal and the panel's logs are read
        // by the server's operator, while the journal is never deleted. The command still travels in
        // and out of this module in full, because it belongs to the customer who wrote it; what it
        // must never do is get written down here.
        //
        // Both halves are exercised: a run that SUCCEEDS (which journals and returns the entry) and
        // one the agent REFUSES (which journals a failure and writes the module's log line). The
        // refusal path is the one where a diagnostic would be written, so a test that only covered
        // the happy path would prove almost nothing.
        const string Secret = "HUNTER2SECRETPASSWORD";
        var command = $"/usr/bin/mysql -p{Secret} --execute='call nightly()'";

        var succeeding = new World();
        var created = await succeeding.HandleAsync(command: command);
        Assert.True(created.IsSuccess);

        var refused = new World();
        refused.Agent.CreateEntryResult = Result<string>.Fail(Error.Of("AgentInvalidInput", ErrorType.Validation));
        var failed = await refused.HandleAsync(command: command);
        Assert.False(failed.IsSuccess);

        foreach (var world in new[] { succeeding, refused })
        {
            Assert.All(world.Logger.Lines, line =>
            {
                Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
                Assert.DoesNotContain(command, line, StringComparison.Ordinal);
            });

            Assert.All(world.Audit.Entries, entry =>
            {
                Assert.DoesNotContain(Secret, entry.Subject, StringComparison.Ordinal);
                Assert.DoesNotContain(command, entry.Subject, StringComparison.Ordinal);
            });
        }

        // And the refusal DID log something, so the assertions above are not passing over an empty
        // list — the emptiest possible way for a "nothing contains the secret" test to be vacuous.
        Assert.NotEmpty(refused.Logger.Lines);
        Assert.NotEmpty(refused.Audit.Entries);
    }

    /// <summary>The agent refusal that is logged names the error code and the subject and nothing else.</summary>
    [Fact]
    public async Task The_agent_refusal_that_is_logged_names_the_error_code_and_the_subject_and_nothing_else()
    {
        // The other half of the rule: the command is absent, but something useful has to be present,
        // or an operator has nothing to act on and the next person deletes the log line as noise.
        var world = new World();
        world.Agent.CreateEntryResult = Result<string>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        await world.HandleAsync();

        var line = Assert.Single(world.Logger.Lines);
        Assert.Contains("AgentSystemFailure", line, StringComparison.Ordinal);
        Assert.Contains(AccountId.ToString(), line, StringComparison.Ordinal);
    }

    /// <summary>Builds one entry the agent reports as already installed.</summary>
    /// <param name="entryId">The identifier the crontab holds it under.</param>
    private static AgentCronEntry Installed(string entryId)
    {
        return new AgentCronEntry(entryId, new AgentCronSchedule("0", "3", "*", "*", "*"), "/usr/bin/backup", true);
    }

    /// <summary>Everything one creation test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<CreateCronEntryCommandHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public CreateCronEntryCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="maxCronEntries">The plan allowance the stubbed account carries.</param>
        public World(int maxCronEntries = 5)
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Handler = new CreateCronEntryCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, maxCronEntries, 5, 1_024)),
                Agent,
                new CronAuditJournal(Audit, currentUser),
                Logger);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="command">The command line to install.</param>
        /// <param name="schedule">The schedule to install it on.</param>
        /// <param name="accountId">The account to install under; the owner by default.</param>
        public async Task<Result<CronEntryDto>> HandleAsync(
            string command = "/usr/bin/backup",
            CronScheduleDto? schedule = null,
            Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new CreateCronEntryCommand(
                    accountId ?? AccountId,
                    schedule ?? new CronScheduleDto("0", "3", "*", "*", "*"),
                    command,
                    "203.0.113.7",
                    "tests"),
                CancellationToken.None);
        }
    }
}
