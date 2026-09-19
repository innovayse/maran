using Maran.Modules.Databases.Commands.RepairDatabaseGrants;
using Maran.Modules.Databases.Services;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;
using AgentGrantRepairReport = Maran.Agent.Client.Services.DbService.GrantRepairReportDto;
using AgentRefusedGrant = Maran.Agent.Client.Services.DbService.RefusedGrantDto;
using AgentRepairedGrant = Maran.Agent.Client.Services.DbService.RepairedGrantDto;

namespace Maran.Modules.Databases.Tests.Commands.RepairDatabaseGrants;

/// <summary>
/// The acting half of the grant repair, and the gate that makes the inspection the only way into it.
/// </summary>
public sealed class RepairDatabaseGrantsCommandHandlerTests
{
    /// <summary>The repair reads a report of its own before it changes anything.</summary>
    /// <remarks>
    /// The property the whole design rests on: an operator cannot reach the rewrite without the panel
    /// having re-classified the host first. The recorded ORDER is what says so — <c>[true, false]</c>
    /// is an inspected repair, <c>[false]</c> is a blind one, and both answer the caller identically.
    /// </remarks>
    [Fact]
    public async Task The_repair_reads_a_report_of_its_own_before_it_changes_anything()
    {
        var agent = Planning(1);
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([true, false], agent.GrantRepairPasses);
    }

    /// <summary>A confirmation that no longer matches the host refuses and sends no repair.</summary>
    /// <remarks>
    /// Both halves are asserted, and the second is the one that matters: a handler that returned the
    /// conflict AFTER acting would satisfy the code assertion exactly. So the double's recorded passes
    /// must be the single report-only one.
    /// </remarks>
    [Fact]
    public async Task A_confirmation_that_no_longer_matches_the_host_refuses_and_sends_no_repair()
    {
        var agent = Planning(2);
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(1), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseGrantRepairPlanChanged", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal([true], agent.GrantRepairPasses);
    }

    /// <summary>A caller who read no report at all is refused, because zero is a figure like any other.</summary>
    /// <remarks>
    /// The shape a naive client takes: post the command with nothing filled in. On a host with rows to
    /// repair the default <c>0</c> does not match, so the gate refuses — which is what makes the
    /// confirmation a gate rather than a formality nobody has to satisfy.
    /// </remarks>
    [Fact]
    public async Task A_caller_who_read_no_report_at_all_is_refused_because_zero_is_a_figure_like_any_other()
    {
        var agent = Planning(3);
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseGrantRepairPlanChanged", result.Error!.Code);
        Assert.Equal([true], agent.GrantRepairPasses);
    }

    /// <summary>The answer reports what the repair DID, never what the plan said it would.</summary>
    /// <remarks>
    /// <para>
    /// The two passes are made to DISAGREE on purpose, which is the only arrangement that can see this:
    /// the plan lists one row under "would repair" and the acting pass lists a different row under
    /// "repaired". A handler that answered with the plan it had just read — an easy and invisible
    /// mistake, since both passes return the same type — would report a rewrite that never happened and
    /// hide the one that did.
    /// </para>
    /// <para>
    /// It also pins the report-only flag, which the mapper takes from the CALLER rather than inferring
    /// from the lists. An inference would be right here and wrong on the case below, where both lists
    /// are empty.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_answer_reports_what_the_repair_did_never_what_the_plan_said_it_would()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(
                2, 1, [], [new AgentRepairedGrant("alice_shop", "alice_shop", [])], [])),
            RepairResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(
                2, 1, [new AgentRepairedGrant("bob_blog", "bob_blog", ["bobxblog"])], [], [])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(1), CancellationToken.None);

        Assert.False(result.Value.IsReportOnly);
        Assert.Empty(result.Value.WouldRepair);
        var repaired = Assert.Single(result.Value.Repaired);
        Assert.Equal("bob_blog", repaired.DatabaseName);
        Assert.Equal(["bobxblog"], repaired.AlsoMatchedDatabases);
    }

    /// <summary>A host with nothing to repair succeeds and says it changed nothing.</summary>
    /// <remarks>
    /// <para>
    /// The inverse control rules/testing.md requires: a gate mutated to refuse everything passes every
    /// test that only ever hands it a mismatch. Here the confirmed figure is zero AND the host has
    /// nothing to repair, so the repair must go through and answer as an action that found nothing —
    /// not as an error, and not as an inspection.
    /// </para>
    /// <para>
    /// <c>IsReportOnly</c> is the assertion with teeth. Both lists are empty, so a mapper that inferred
    /// the flag from them would report this ACTION as an inspection, and an operator would be left
    /// unsure whether the repair had run.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_host_with_nothing_to_repair_succeeds_and_says_it_changed_nothing()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(9, 9, [], [], [])),
            RepairResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(9, 9, [], [], [])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(0), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReportOnly);
        Assert.Equal(9u, result.Value.AlreadyCorrect);
        Assert.Empty(result.Value.Repaired);
        Assert.Equal([true, false], agent.GrantRepairPasses);
    }

    /// <summary>A refused row survives the acting pass with its sentence and its action.</summary>
    /// <remarks>
    /// The repair leaves refused rows exactly as it found them, so they appear in the acting pass too —
    /// and they are the rows the operator still has work to do about. A mapping that named them only on
    /// the report would leave the post-repair screen silent about everything the panel refused to touch.
    /// </remarks>
    [Fact]
    public async Task A_refused_row_survives_the_acting_pass_with_its_sentence_and_its_action()
    {
        var refused = new AgentRefusedGrant("localhost", "ops_metrics", "grafana", "NotThePanelsNaming");
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(1, 0, [], [], [refused])),
            RepairResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(1, 0, [], [], [refused])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(0), CancellationToken.None);

        var row = Assert.Single(result.Value.Refused);
        Assert.Equal("grafana", row.DbUsername);
        Assert.NotEqual(row.Reason, row.ReasonDisplayName);
        Assert.NotEqual(row.Reason, row.ReasonAdvice);
    }

    /// <summary>An agent that refuses the planning pass stops the repair before it is attempted.</summary>
    [Fact]
    public async Task An_agent_that_refuses_the_planning_pass_stops_the_repair_before_it_is_attempted()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal([true], agent.GrantRepairPasses);
    }

    /// <summary>An agent that refuses the acting pass is reported as the failure it is.</summary>
    /// <remarks>
    /// The mirror of the case above, and it is not the same test: the gate has already passed here, so
    /// the failure arrives after the panel has decided to act. It must not be reported as a plan that
    /// changed — the operator's figure was right, and the server is what refused.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_refuses_the_acting_pass_is_reported_as_the_failure_it_is()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(9, 9, [], [], [])),
            RepairResult = Result<AgentGrantRepairReport>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairDatabaseGrantsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal([true, false], agent.GrantRepairPasses);
    }

    /// <summary>A repair that took effect leaves one journal entry naming who ran it and the counts.</summary>
    /// <remarks>
    /// <para>
    /// This operation rewrites live database access for every customer on the host at once, and the
    /// agent's own log cannot say which operator asked for it. The panel's journal is the only
    /// attributable record, so its absence is a defect of the same kind as a missing refusal: silent,
    /// green, and discovered only when somebody needs to account for the change.
    /// </para>
    /// <para>
    /// The subject is asserted EXACTLY rather than for containment, for two reasons. A <c>Db</c> or
    /// <c>User</c> column must not appear in it — a refused row's columns belong to another tenant and
    /// this journal is never deleted — and a containment assertion would pass on an entry that named
    /// the counts and a stranger's database beside them. The second reason is the shape: the subject is
    /// <c>key=value;key=value</c> and not a sentence, because a live check found the earlier prose form
    /// rendered in the audit screen's subject column in english on a russian page, where every other
    /// subject is an identifier. Only an exact assertion pins a format.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_repair_that_took_effect_leaves_one_journal_entry_naming_who_ran_it_and_the_counts()
    {
        var agent = Planning(2);
        agent.RepairResult = Result<AgentGrantRepairReport>.Ok(
            new AgentGrantRepairReport(
                9,
                6,
                [new AgentRepairedGrant("alice_db0", "alice_db0", []), new AgentRepairedGrant("alice_db1", "alice_db1", [])],
                [],
                [new AgentRefusedGrant("localhost", "other_db", "other_user", "NotThePanelsNaming")]));
        var audit = new RecordingAuditWriter();
        var handler = HandlerOver(agent, audit);

        var result = await handler.HandleAsync(
            new RepairDatabaseGrantsCommand(2, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("DatabaseGrantsRepaired", entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("examined=9;narrowed=2;refused=1", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    /// <summary>A refused repair is journalled too, with the two figures that disagreed.</summary>
    /// <remarks>
    /// The inverse control of the case above, and a record in its own right: a mismatch is either an
    /// ordinary race between two administrators or somebody attempting the write without having read a
    /// report, and only a journalled refusal makes the second visible as a pattern. <c>Succeeded</c> is
    /// asserted <c>false</c> because an entry that recorded the attempt as a success would be worse
    /// than none — it would say the host was rewritten when nothing was touched.
    /// </remarks>
    [Fact]
    public async Task A_refused_repair_is_journalled_too_with_the_two_figures_that_disagreed()
    {
        var agent = Planning(2);
        var audit = new RecordingAuditWriter();
        var handler = HandlerOver(agent, audit);

        var result = await handler.HandleAsync(
            new RepairDatabaseGrantsCommand(1, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("DatabaseGrantsRepaired", entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal("confirmed=1;hostReports=2", entry.Subject);
        Assert.Equal([true], agent.GrantRepairPasses);
    }

    /// <summary>Builds the handler over a double and an audit writer the test can read back.</summary>
    /// <param name="agent">The recording agent double.</param>
    /// <param name="audit">The writer whose entries the journal assertions read.</param>
    /// <returns>The handler under test.</returns>
    private static RepairDatabaseGrantsCommandHandler HandlerOver(
        RecordingAgentDbClient agent,
        RecordingAuditWriter audit)
    {
        return new RepairDatabaseGrantsCommandHandler(
            agent,
            DatabasesTestContext.RefusalText(),
            new DatabaseAuditJournal(audit, FakeCurrentUser.Admin()));
    }

    /// <summary>Builds a double whose planning pass reports <paramref name="wouldRepair"/> rows.</summary>
    /// <param name="wouldRepair">How many rows the plan says would be rewritten.</param>
    /// <returns>The configured double; its acting pass answers an empty census.</returns>
    private static RecordingAgentDbClient Planning(int wouldRepair)
    {
        var planned = Enumerable.Range(0, wouldRepair)
            .Select(index =>
            {
                return new AgentRepairedGrant($"alice_db{index}", $"alice_db{index}", []);
            })
            .ToList();

        return new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(
                new AgentGrantRepairReport((uint)wouldRepair, 0, [], planned, [])),
        };
    }
}
