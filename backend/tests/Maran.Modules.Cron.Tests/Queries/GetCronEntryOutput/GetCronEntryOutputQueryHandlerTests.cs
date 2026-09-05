using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.GetCronEntryOutput;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Queries.GetCronEntryOutput;

/// <summary>What reading a cron entry's last run answers, including when there has not been one.</summary>
public sealed class GetCronEntryOutputQueryHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Another tenants account is answered not found and its entry is never read.</summary>
    [Fact]
    public async Task Another_tenants_account_is_answered_not_found_and_its_entry_is_never_read()
    {
        var world = new World();

        var result = await world.HandleAsync(StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.OutputReads);
    }

    /// <summary>An entry that has never run answers nothing rather than a run that said nothing.</summary>
    [Fact]
    public async Task An_entry_that_has_never_run_answers_nothing_rather_than_a_run_that_said_nothing()
    {
        // All three fields have meaningful defaults — an empty string is a run that printed nothing,
        // zero is a successful exit, and zero seconds is the epoch — so flattening "never run" into
        // a reading would tell a customer debugging a job that never fires that it ran and succeeded.
        var world = new World();

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /// <summary>A run that printed nothing is reported as an empty output and not as a missing run.</summary>
    [Fact]
    public async Task A_run_that_printed_nothing_is_reported_as_an_empty_output_and_not_as_a_missing_run()
    {
        // The other side of the same distinction: the ordinary outcome of a healthy job is a run
        // that says nothing, and it must not read as "this has never run".
        var world = new World();
        world.Agent.GetEntryOutputResult = Result<AgentCronRunOutput?>.Ok(
            new AgentCronRunOutput(string.Empty, 0, 1_772_000_000));

        var result = await world.HandleAsync();

        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value.Output);
        Assert.Equal(0, result.Value.LastExitCode);
        Assert.Equal(1_772_000_000, result.Value.LastRunAtUnix);
        Assert.Equal(EntryId, result.Value.EntryId);
    }

    /// <summary>An entry the crontab does not hold is answered not found.</summary>
    [Fact]
    public async Task An_entry_the_crontab_does_not_hold_is_answered_not_found()
    {
        var world = new World();
        world.Agent.GetEntryOutputResult = Result<AgentCronRunOutput?>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("CronEntryNotFound", result.Error!.Code);
    }

    /// <summary>Everything one output test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on; it stands in for the crontab.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>Every line the handler logged, rendered as a sink would write it.</summary>
        public CapturingLogger<GetCronEntryOutputQueryHandler> Logger { get; } = new();

        /// <summary>The handler under test.</summary>
        public GetCronEntryOutputQueryHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        public World()
        {
            Handler = new GetCronEntryOutputQueryHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                Logger);
        }

        /// <summary>Runs the handler.</summary>
        /// <param name="accountId">The account whose entry to read; the owner by default.</param>
        public async Task<Result<CronEntryOutputDto?>> HandleAsync(Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new GetCronEntryOutputQuery(accountId ?? AccountId, EntryId), CancellationToken.None);
        }
    }
}
