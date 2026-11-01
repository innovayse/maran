using Maran.Agent.Client.Services.CronService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.CronService;

/// <summary>Mapping contract of <see cref="AgentCronClient.RunEntryAsync"/> (issue #158).</summary>
public sealed class AgentCronClientRunTests
{
    /// <summary>A run sends the account and the entry, and nothing else.</summary>
    [Fact]
    public async Task A_run_sends_the_account_and_the_entry()
    {
        var stub = new StubCronService { RunResponse = new RunCronEntryResponse { Ok = new RunCronEntryOk() } };

        await Client(stub).RunEntryAsync("alice", "e1", CancellationToken.None);

        var request = Assert.IsType<RunCronEntryRequest>(stub.LastRunRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("e1", request.EntryId);
    }

    /// <summary>A run that ended on its own maps its duration, status and both streams, and no limit.</summary>
    [Fact]
    public async Task A_run_that_ended_on_its_own_maps_its_duration_status_and_streams()
    {
        var stub = new StubCronService
        {
            RunResponse = new RunCronEntryResponse
            {
                Ok = new RunCronEntryOk { DurationMs = 1500, ExitCode = 3, Stdout = "out", Stderr = "err" },
            },
        };

        var result = await Client(stub).RunEntryAsync("alice", "e1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AgentCronRun(1500, 3, "out", "err", null), result.Value);
    }

    /// <summary>A stopped run carries its ceiling and no status rather than a zero that reads as success.</summary>
    [Fact]
    public async Task A_stopped_run_carries_its_ceiling_and_no_status()
    {
        var stub = new StubCronService
        {
            RunResponse = new RunCronEntryResponse
            {
                Ok = new RunCronEntryOk { DurationMs = 120_000, Limit = CronRunLimit.Runtime },
            },
        };

        var result = await Client(stub).RunEntryAsync("alice", "e1", CancellationToken.None);

        Assert.Null(result.Value.ExitCode);
        Assert.Equal(AgentCronRunLimit.Runtime, result.Value.Limit);
    }

    /// <summary>A suspended account maps to its own conflict code.</summary>
    [Fact]
    public async Task A_suspended_account_maps_to_its_own_conflict_code()
    {
        var stub = new StubCronService
        {
            RunResponse = new RunCronEntryResponse
            {
                Error = new AgentError { Code = ErrorCode.AccountSuspended, Message = "suspended" },
            },
        };

        var result = await Client(stub).RunEntryAsync("alice", "e1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAccountSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>A response with neither branch set is refused rather than read as a run.</summary>
    [Fact]
    public async Task A_response_with_neither_branch_set_is_refused()
    {
        var stub = new StubCronService { RunResponse = new RunCronEntryResponse() };

        var result = await Client(stub).RunEntryAsync("alice", "e1", CancellationToken.None);

        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The client under test over <paramref name="stub"/>.</summary>
    /// <param name="stub">The invoker the client calls.</param>
    /// <returns>The client.</returns>
    private static AgentCronClient Client(StubCronService stub)
    {
        return new AgentCronClient(stub, NullLogger<AgentCronClient>.Instance);
    }
}
