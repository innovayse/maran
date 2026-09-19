using Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AgentHomeGroupRepairReport = Maran.Agent.Client.Services.AccountsService.HomeGroupRepairReportDto;
using AgentRefusedHome = Maran.Agent.Client.Services.AccountsService.RefusedHomeDto;
using AgentRepairedHome = Maran.Agent.Client.Services.AccountsService.RepairedHomeDto;

namespace Maran.Modules.Accounts.Tests.Commands.RepairAccountHomeGroups;

/// <summary>
/// The acting half of the home-group repair, and the gate that makes the inspection the only way
/// into it. Mirrors <c>RepairDatabaseGrantsCommandHandlerTests</c> exactly: the same report/confirm
/// shape, tested the same way.
/// </summary>
public sealed class RepairAccountHomeGroupsCommandHandlerTests
{
    /// <summary>The repair reads a report of its own before it changes anything.</summary>
    /// <remarks>
    /// The property the whole design rests on: an operator cannot reach the rewrite without the panel
    /// having re-classified the host first. The recorded ORDER is what says so — <c>[true, false]</c>
    /// is an inspected repair, <c>[true]</c> is a blind refusal, and both answer the caller
    /// identically at the code level.
    /// </remarks>
    [Fact]
    public async Task The_repair_reads_a_report_of_its_own_before_it_changes_anything()
    {
        var agent = Planning(1);
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([true, false], agent.HomeGroupRepairPasses);
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

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(1), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountHomeGroupRepairPlanChanged", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal([true], agent.HomeGroupRepairPasses);
    }

    /// <summary>A caller who read no report at all is refused, because zero is a figure like any other.</summary>
    [Fact]
    public async Task A_caller_who_read_no_report_at_all_is_refused_because_zero_is_a_figure_like_any_other()
    {
        var agent = Planning(3);
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountHomeGroupRepairPlanChanged", result.Error!.Code);
        Assert.Equal([true], agent.HomeGroupRepairPasses);
    }

    /// <summary>The answer reports what the repair DID, never what the plan said it would.</summary>
    /// <remarks>
    /// The two passes are made to DISAGREE on purpose, which is the only arrangement that can see
    /// this: the plan lists one home under "would repair" and the acting pass lists a different home
    /// under "repaired". A handler that answered with the plan it had just read — an easy and
    /// invisible mistake, since both passes return the same type — would report a re-group that never
    /// happened and hide the one that did.
    /// </remarks>
    [Fact]
    public async Task The_answer_reports_what_the_repair_did_never_what_the_plan_said_it_would()
    {
        var agent = new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Ok(new AgentHomeGroupRepairReport(
                2, 1, [], [new AgentRepairedHome("alice", "/home/alice")], [])),
            HomeGroupRepairResult = Result<AgentHomeGroupRepairReport>.Ok(new AgentHomeGroupRepairReport(
                2, 1, [new AgentRepairedHome("bob", "/home/bob")], [], [])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(1), CancellationToken.None);

        Assert.False(result.Value.IsReportOnly);
        Assert.Empty(result.Value.WouldRepair);
        var repaired = Assert.Single(result.Value.Repaired);
        Assert.Equal("bob", repaired.AccountUsername);
    }

    /// <summary>A host with nothing to repair succeeds and says it changed nothing.</summary>
    /// <remarks>
    /// The inverse control rules/testing.md requires: a gate mutated to refuse everything passes every
    /// test that only ever hands it a mismatch. Here the confirmed figure is zero AND the host has
    /// nothing to repair, so the repair must go through and answer as an action that found nothing —
    /// not as an error, and not as an inspection.
    /// </remarks>
    [Fact]
    public async Task A_host_with_nothing_to_repair_succeeds_and_says_it_changed_nothing()
    {
        var agent = new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport(9, 9, [], [], [])),
            HomeGroupRepairResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport(9, 9, [], [], [])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(0), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReportOnly);
        Assert.Equal(9u, result.Value.AlreadyCorrect);
        Assert.Empty(result.Value.Repaired);
        Assert.Equal([true, false], agent.HomeGroupRepairPasses);
    }

    /// <summary>A refused home survives the acting pass with its sentence and its advice.</summary>
    /// <remarks>
    /// The repair leaves refused homes exactly as it found them, so they appear in the acting pass
    /// too — and they are the homes the operator still has work to do about.
    /// </remarks>
    [Fact]
    public async Task A_refused_home_survives_the_acting_pass_with_its_sentence_and_its_advice()
    {
        var refused = new AgentRefusedHome("carol", "/home/carol", "OwnerMismatch");
        var agent = new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport(1, 0, [], [], [refused])),
            HomeGroupRepairResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport(1, 0, [], [], [refused])),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(0), CancellationToken.None);

        var row = Assert.Single(result.Value.Refused);
        Assert.Equal("carol", row.AccountUsername);
        Assert.NotEqual(row.Reason, row.ReasonDisplayName);
        Assert.NotEqual(row.Reason, row.ReasonAdvice);
    }

    /// <summary>An agent that refuses the planning pass stops the repair before it is attempted.</summary>
    [Fact]
    public async Task An_agent_that_refuses_the_planning_pass_stops_the_repair_before_it_is_attempted()
    {
        var agent = new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal([true], agent.HomeGroupRepairPasses);
    }

    /// <summary>An agent that refuses the acting pass is reported as the failure it is.</summary>
    /// <remarks>
    /// The mirror of the case above, and it is not the same test: the gate has already passed here,
    /// so the failure arrives after the panel has decided to act. It must not be reported as a plan
    /// that changed — the operator's figure was right, and the host is what refused.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_refuses_the_acting_pass_is_reported_as_the_failure_it_is()
    {
        var agent = new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport(9, 9, [], [], [])),
            HomeGroupRepairResult = Result<AgentHomeGroupRepairReport>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = HandlerOver(agent, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new RepairAccountHomeGroupsCommand(0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal([true, false], agent.HomeGroupRepairPasses);
    }

    /// <summary>A repair that took effect leaves one journal entry naming who ran it and the counts.</summary>
    /// <remarks>
    /// The subject is asserted EXACTLY, never for containment: an account name must not appear in it —
    /// per this handler's own remarks, a refused row's account may not be the panel's own naming for
    /// that account, so a name in the trail could be wrong in a way this code cannot detect — and the
    /// shape is <c>key=value;key=value</c>, never a sentence.
    /// </remarks>
    [Fact]
    public async Task A_repair_that_took_effect_leaves_one_journal_entry_naming_who_ran_it_and_the_counts()
    {
        var agent = Planning(2);
        agent.HomeGroupRepairResult = Result<AgentHomeGroupRepairReport>.Ok(
            new AgentHomeGroupRepairReport(
                9,
                6,
                [new AgentRepairedHome("alice", "/home/alice"), new AgentRepairedHome("bob", "/home/bob")],
                [],
                [new AgentRefusedHome("carol", "/home/carol", "OwnerMismatch")]));
        var audit = new RecordingAuditWriter();
        var handler = HandlerOver(agent, audit);

        var result = await handler.HandleAsync(
            new RepairAccountHomeGroupsCommand(2, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("AccountHomeGroupsRepaired", entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("examined=9;repaired=2;refused=1", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    /// <summary>A refused repair is journalled too, with the two figures that disagreed.</summary>
    /// <remarks>
    /// The inverse control of the case above: a mismatch is either an ordinary race between two
    /// administrators or somebody attempting the write without having read a report, and only a
    /// journalled refusal makes the second visible as a pattern. <c>Succeeded</c> is asserted
    /// <c>false</c> because an entry that recorded the attempt as a success would say the host was
    /// rewritten when nothing was touched.
    /// </remarks>
    [Fact]
    public async Task A_refused_repair_is_journalled_too_with_the_two_figures_that_disagreed()
    {
        var agent = Planning(2);
        var audit = new RecordingAuditWriter();
        var handler = HandlerOver(agent, audit);

        var result = await handler.HandleAsync(
            new RepairAccountHomeGroupsCommand(1, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("AccountHomeGroupsRepaired", entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal("confirmed=1;hostReports=2", entry.Subject);
        Assert.Equal([true], agent.HomeGroupRepairPasses);
    }

    /// <summary>Builds the handler over a double and an audit writer the test can read back.</summary>
    private static RepairAccountHomeGroupsCommandHandler HandlerOver(
        RecordingAgentAccountsClient agent,
        RecordingAuditWriter audit)
    {
        return new RepairAccountHomeGroupsCommandHandler(
            agent,
            RefusalText(),
            new AccountAuditJournal(audit, FakeCurrentUser.Admin()));
    }

    /// <summary>Builds a double whose planning pass reports <paramref name="wouldRepair"/> homes.</summary>
    /// <param name="wouldRepair">How many homes the plan says would be re-grouped.</param>
    /// <returns>The configured double; its acting pass answers an empty census.</returns>
    private static RecordingAgentAccountsClient Planning(int wouldRepair)
    {
        var planned = Enumerable.Range(0, wouldRepair)
            .Select(index =>
            {
                return new AgentRepairedHome($"account{index}", $"/home/account{index}");
            })
            .ToList();

        return new RecordingAgentAccountsClient
        {
            HomeGroupReportResult = Result<AgentHomeGroupRepairReport>.Ok(
                new AgentHomeGroupRepairReport((uint)wouldRepair, 0, [], planned, [])),
        };
    }

    /// <summary>Builds a real localizer over this module's own embedded resources.</summary>
    private static HomeGroupRepairRefusalDisplayNames RefusalText()
    {
        return new HomeGroupRepairRefusalDisplayNames(new StringLocalizer<DisplayNames>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance)));
    }
}
