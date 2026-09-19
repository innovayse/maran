using Maran.Modules.Databases.Queries.InspectDatabaseGrants;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;
using AgentGrantRepairReport = Maran.Agent.Client.Services.DbService.GrantRepairReportDto;
using AgentRefusedGrant = Maran.Agent.Client.Services.DbService.RefusedGrantDto;
using AgentRepairedGrant = Maran.Agent.Client.Services.DbService.RepairedGrantDto;

namespace Maran.Modules.Databases.Tests.Queries.InspectDatabaseGrants;

/// <summary>
/// The inspection half of the grant repair: what an operator is shown BEFORE anything on the host
/// changes.
/// </summary>
public sealed class InspectDatabaseGrantsQueryHandlerTests
{
    /// <summary>The inspection asks the agent for a report-only pass, and for nothing else.</summary>
    /// <remarks>
    /// The load-bearing property of this handler, and the only one whose failure is silent: a query
    /// that passed <c>reportOnly: false</c> would rewrite every customer's database access on a
    /// <c>GET</c>, answer with a report that looked exactly like an inspection, and pass any test that
    /// only read the response. So the flag the double RECORDED is asserted, not the shape of the
    /// answer.
    /// </remarks>
    [Fact]
    public async Task The_inspection_asks_the_agent_for_a_report_only_pass_and_for_nothing_else()
    {
        var agent = new RecordingAgentDbClient();
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([true], agent.GrantRepairPasses);
    }

    /// <summary>A host with nothing to repair is a successful census and not an empty failure.</summary>
    /// <remarks>
    /// The inverse control rules/testing.md requires beside every refusal in this slice: the ordinary
    /// answer on a clean installation is "eleven rows read, all already correct", and a screen shown a
    /// failure for that would send an operator hunting a defect that is not there. It asserts the
    /// COUNTS rather than merely <c>IsSuccess</c>, because a handler that dropped the census and
    /// returned an empty one would satisfy the flag alone.
    /// </remarks>
    [Fact]
    public async Task A_host_with_nothing_to_repair_is_a_successful_census_and_not_an_empty_failure()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(11, 11, [], [], [])),
        };
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsReportOnly);
        Assert.Equal(11u, result.Value.ExaminedGrants);
        Assert.Equal(11u, result.Value.AlreadyCorrect);
        Assert.Empty(result.Value.WouldRepair);
        Assert.Empty(result.Value.Refused);
    }

    /// <summary>A refused row arrives with a sentence and an action, never with the machine reason alone.</summary>
    /// <remarks>
    /// rules/vue.md forbids machine text on screen and the SPA holds no domain vocabulary, so if the
    /// two sentences are not on the wire they exist nowhere. Both are asserted to differ from the
    /// machine reason: a resolver whose lookup silently missed would answer with the identifier, which
    /// is non-empty and would satisfy a <c>NotNull</c>.
    /// </remarks>
    [Fact]
    public async Task A_refused_row_arrives_with_a_sentence_and_an_action_never_with_the_machine_reason_alone()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(
                1,
                0,
                [],
                [],
                [new AgentRefusedGrant("10.0.0.5", "reporting", "reporting_tool", "HostIsNotLocalhost")])),
        };
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        var refused = Assert.Single(result.Value.Refused);
        Assert.Equal("HostIsNotLocalhost", refused.Reason);
        Assert.NotEqual(refused.Reason, refused.ReasonDisplayName);
        Assert.NotEqual(refused.Reason, refused.ReasonAdvice);
        Assert.NotEqual(refused.ReasonDisplayName, refused.ReasonAdvice);
    }

    /// <summary>The server's raw columns reach the operator exactly as the server holds them.</summary>
    /// <remarks>
    /// A refused row is refused because it is not a value this panel could have written, so the escapes
    /// are the evidence. A double-escaped name is used deliberately: it is the shape that fooled the
    /// agent lane's own assertion, and a mapper that "tidied" it would leave an operator comparing a
    /// rendering against their server.
    /// </remarks>
    [Fact]
    public async Task The_servers_raw_columns_reach_the_operator_exactly_as_the_server_holds_them()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(
                1,
                0,
                [],
                [],
                [new AgentRefusedGrant("localhost", @"alice\\_shop", "alice_shop", "PartiallyOrUnfamiliarlyEscaped")])),
        };
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        var refused = Assert.Single(result.Value.Refused);
        Assert.Equal("localhost", refused.GrantHost);
        Assert.Equal(@"alice\\_shop", refused.DatabaseName);
        Assert.Equal("alice_shop", refused.DbUsername);
    }

    /// <summary>A would-repair row carries the databases the old pattern also reached.</summary>
    /// <remarks>
    /// The one piece of evidence this operation can produce, and the report is the only place an
    /// operator can read it before deciding. A mapping that dropped the list would leave the screen
    /// unable to say anything about exposure at all, and every assertion about counts would still pass.
    /// </remarks>
    [Fact]
    public async Task A_would_repair_row_carries_the_databases_the_old_pattern_also_reached()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Ok(new AgentGrantRepairReport(
                1,
                0,
                [],
                [new AgentRepairedGrant("alice_shop", "alice_shop", ["alicexshop", "alice_shop"])],
                [])),
        };
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        Assert.Empty(result.Value.Repaired);
        var planned = Assert.Single(result.Value.WouldRepair);
        Assert.Equal(["alicexshop", "alice_shop"], planned.AlsoMatchedDatabases);
    }

    /// <summary>The agent's own refusal is returned as its typed error and not as an empty report.</summary>
    /// <remarks>
    /// An unreachable agent must not read as "this host has no grants": that answer is a census of
    /// nothing presented as a census of the server, which is exactly the reading that would let an
    /// operator conclude their host was clean.
    /// </remarks>
    [Fact]
    public async Task The_agents_own_refusal_is_returned_as_its_typed_error_and_not_as_an_empty_report()
    {
        var agent = new RecordingAgentDbClient
        {
            ReportResult = Result<AgentGrantRepairReport>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new InspectDatabaseGrantsQueryHandler(agent, DatabasesTestContext.RefusalText());

        var result = await handler.HandleAsync(new InspectDatabaseGrantsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }
}
