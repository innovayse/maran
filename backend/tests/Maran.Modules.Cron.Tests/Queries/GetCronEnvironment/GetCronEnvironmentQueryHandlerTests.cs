using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.GetCronEnvironment;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Queries.GetCronEnvironment;

/// <summary>Whose environment a read returns, and in what form.</summary>
public sealed class GetCronEnvironmentQueryHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Another tenants account is answered not found and its crontab is never read.</summary>
    [Fact]
    public async Task Another_tenants_account_is_answered_not_found_and_its_crontab_is_never_read()
    {
        var world = new World();

        var result = await world.HandleAsync(StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.EnvironmentReads);
    }

    /// <summary>The assignments come back in the order the crontab holds them and with their values.</summary>
    [Fact]
    public async Task The_assignments_come_back_in_the_order_the_crontab_holds_them_and_with_their_values()
    {
        // The values go back to their owner in full, for the reason the command does: they are the
        // customer's own, and a screen that hid them would leave them unable to edit their own
        // preamble. They still reach no log line and no journal row on the way.
        var world = new World();
        world.Agent.GetEnvironmentResult = Result<IReadOnlyList<AgentCronEnvVar>>.Ok(
        [
            new AgentCronEnvVar("PATH", "/usr/local/bin:/usr/bin"),
            new AgentCronEnvVar("DATABASE_URL", "postgres://user:HUNTER2@db.internal/app"),
        ]);

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["PATH", "DATABASE_URL"], result.Value.Select(variable =>
        {
            return variable.Name;
        }));
        Assert.Equal("postgres://user:HUNTER2@db.internal/app", result.Value[1].Value);
    }

    /// <summary>A crontab with no managed assignments answers an empty set rather than a failure.</summary>
    [Fact]
    public async Task A_crontab_with_no_managed_assignments_answers_an_empty_set_rather_than_a_failure()
    {
        var world = new World();

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>A crontab the agent cannot read is a failure and never an empty set.</summary>
    [Fact]
    public async Task A_crontab_the_agent_cannot_read_is_a_failure_and_never_an_empty_set()
    {
        // An empty set is what a caller edits and sends back, and sending it back CLEARS everything.
        // Reporting an outage as "no assignments" would hand a customer a screen whose save button
        // deletes their preamble.
        var world = new World();
        world.Agent.GetEnvironmentResult =
            Result<IReadOnlyList<AgentCronEnvVar>>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronOperationFailed", result.Error!.Code);
    }

    /// <summary>Everything one environment read needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<GetCronEnvironmentQueryHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public GetCronEnvironmentQueryHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            Handler = new GetCronEnvironmentQueryHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                Logger);
        }

        /// <summary>Runs the handler.</summary>
        /// <param name="accountId">The account whose crontab to read; the owner by default.</param>
        public async Task<Result<IReadOnlyList<CronEnvironmentVariableDto>>> HandleAsync(Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new GetCronEnvironmentQuery(accountId ?? AccountId), CancellationToken.None);
        }
    }
}
