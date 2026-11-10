using Maran.Agent.Client.Services.TransferService;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>
/// What the pull of an account's files and the transfer of a database become: typed events and exactly one
/// ending, whatever the agent did (issue #197, phase 2).
/// </summary>
public sealed class TransferStreamsTests
{
    /// <summary>The source the tests aim at.</summary>
    private static readonly AgentSourceEndpoint Endpoint = new("203.0.113.9", 22, "root");

    /// <summary>The credential every call sends.</summary>
    private static readonly AgentSourceCredential Credential =
        new(new SensitiveString("PRIVATE-KEY-SENTINEL"), "ssh-ed25519 AAAAx");

    /// <summary>A healthy pull becomes progress and done, with the measured size.</summary>
    [Fact]
    public async Task A_complete_pull_becomes_progress_then_done()
    {
        var stub = new StubTransferService();
        stub.PullResponses.Add(new Wire.PullAccountFilesResponse { Progress = new Wire.PullAccountFilesProgress { SlicesCompleted = 1 } });
        stub.PullResponses.Add(new Wire.PullAccountFilesResponse
        {
            Done = new Wire.PullAccountFilesDone { Slices = 2, SourceBytes = 4096, SourceBytesKnown = true },
        });

        var events = await DrainPullAsync(NewClient(stub));

        Assert.Equal(1, Assert.IsType<AgentFilePullProgress>(events[0]).SlicesCompleted);
        var done = Assert.IsType<AgentFilePullDone>(events[1]);
        Assert.Equal((2, 4096L), (done.Slices, done.SourceBytes));
    }

    /// <summary>A size the agent did not measure is not reported as an empty home.</summary>
    [Fact]
    public async Task An_unmeasured_source_size_is_null_and_not_zero()
    {
        var stub = new StubTransferService();
        stub.PullResponses.Add(new Wire.PullAccountFilesResponse { Done = new Wire.PullAccountFilesDone { Slices = 1 } });

        var events = await DrainPullAsync(NewClient(stub));

        Assert.Null(Assert.IsType<AgentFilePullDone>(Assert.Single(events)).SourceBytes);
    }

    /// <summary>A pull stream that ends with no terminal message is a failure, never a quiet completion.</summary>
    [Fact]
    public async Task A_pull_that_ends_without_a_terminal_message_yields_a_failed_event()
    {
        var stub = new StubTransferService();
        stub.PullResponses.Add(new Wire.PullAccountFilesResponse { Progress = new Wire.PullAccountFilesProgress { SlicesCompleted = 1 } });

        var events = await DrainPullAsync(NewClient(stub));

        Assert.Equal("AgentInvalidResponse", Assert.IsType<AgentFilePullFailed>(events[^1]).Error.Code);
        Assert.DoesNotContain(events, entry => { return entry is AgentFilePullDone; });
    }

    /// <summary>The agent's terminal error carries its code, the connection reason and the transfer reason.</summary>
    [Fact]
    public async Task A_pull_error_carries_both_reasons()
    {
        var stub = new StubTransferService();
        stub.PullResponses.Add(new Wire.PullAccountFilesResponse
        {
            Error = new Wire.AgentError { Code = Wire.ErrorCode.BelowFreeSpaceFloor, Message = "the disk" },
            FailureReason = Wire.SourceFailureReason.Unreachable,
            TransferFailureReason = Wire.TransferFailureReason.DiskInsufficient,
        });

        var failed = Assert.IsType<AgentFilePullFailed>(Assert.Single(await DrainPullAsync(NewClient(stub))));

        Assert.Equal(SourceFailureReason.Unreachable, failed.Reason);
        Assert.Equal(TransferFailureReason.DiskInsufficient, failed.TransferReason);
    }

    /// <summary>The request on the wire carries the account, the home, the phase and the limit, and the key bytes.</summary>
    [Fact]
    public async Task The_pull_request_reaches_the_wire_with_every_field()
    {
        var stub = new StubTransferService();

        await DrainPullAsync(NewClient(stub), AgentPullPhase.Final, bandwidthLimitKib: 2048);

        var wire = stub.LastPullRequest!;
        Assert.Equal("alice", wire.AccountUsername);
        Assert.Equal("/home/alice", wire.SourceHome);
        Assert.Equal(Wire.PullPhase.Final, wire.Phase);
        Assert.Equal(2048u, wire.BandwidthLimitKib);
        Assert.Equal(4096UL, wire.ExpectedBytes);
        Assert.Equal("PRIVATE-KEY-SENTINEL", wire.Credential.PrivateKey.ToStringUtf8());
    }

    /// <summary>The initial phase is the default, and an unlimited transfer sends a zero limit.</summary>
    [Fact]
    public async Task The_initial_phase_and_no_limit_are_the_plain_request()
    {
        var stub = new StubTransferService();

        await DrainPullAsync(NewClient(stub));

        Assert.Equal(Wire.PullPhase.Initial, stub.LastPullRequest!.Phase);
        Assert.Equal(0u, stub.LastPullRequest.BandwidthLimitKib);
    }

    /// <summary>A healthy database transfer becomes its two steps and done.</summary>
    [Fact]
    public async Task A_complete_database_transfer_becomes_two_steps_then_done()
    {
        var stub = new StubTransferService();
        stub.DatabaseResponses.Add(new Wire.StreamRemoteDatabaseResponse { Progress = new Wire.StreamRemoteDatabaseProgress { Step = Wire.DatabaseTransferStep.Dumping } });
        stub.DatabaseResponses.Add(new Wire.StreamRemoteDatabaseResponse { Progress = new Wire.StreamRemoteDatabaseProgress { Step = Wire.DatabaseTransferStep.Loading } });
        stub.DatabaseResponses.Add(new Wire.StreamRemoteDatabaseResponse { Done = new Wire.StreamRemoteDatabaseDone { DumpBytes = 77 } });

        var events = await DrainDatabaseAsync(NewClient(stub));

        Assert.Equal(DatabaseTransferStep.Dumping, Assert.IsType<AgentDatabaseTransferProgress>(events[0]).Step);
        Assert.Equal(DatabaseTransferStep.Loading, Assert.IsType<AgentDatabaseTransferProgress>(events[1]).Step);
        Assert.Equal(77L, Assert.IsType<AgentDatabaseTransferDone>(events[2]).DumpBytes);
    }

    /// <summary>A refused dump is a failure that says the loader refused it.</summary>
    [Fact]
    public async Task A_refused_dump_carries_the_load_refused_reason()
    {
        var stub = new StubTransferService();
        stub.DatabaseResponses.Add(new Wire.StreamRemoteDatabaseResponse
        {
            Error = new Wire.AgentError { Code = Wire.ErrorCode.ValidationFailed, Message = "refused" },
            TransferFailureReason = Wire.TransferFailureReason.LoadRefused,
        });

        var failed = Assert.IsType<AgentDatabaseTransferFailed>(Assert.Single(await DrainDatabaseAsync(NewClient(stub))));

        Assert.Equal(TransferFailureReason.LoadRefused, failed.TransferReason);
    }

    /// <summary>A database transfer that ends with no terminal message is a failure.</summary>
    [Fact]
    public async Task A_database_transfer_that_ends_without_a_terminal_message_yields_a_failed_event()
    {
        var stub = new StubTransferService();
        stub.DatabaseResponses.Add(new Wire.StreamRemoteDatabaseResponse { Progress = new Wire.StreamRemoteDatabaseProgress { Step = Wire.DatabaseTransferStep.Dumping } });

        var events = await DrainDatabaseAsync(NewClient(stub));

        Assert.Equal("AgentInvalidResponse", Assert.IsType<AgentDatabaseTransferFailed>(events[^1]).Error.Code);
    }

    /// <summary>The panel's client over the stub.</summary>
    /// <param name="stub">The scripted transport.</param>
    /// <returns>The client.</returns>
    private static AgentTransferClient NewClient(StubTransferService stub)
    {
        return new AgentTransferClient(stub, NullLogger<AgentTransferClient>.Instance);
    }

    /// <summary>Reads the whole pull stream.</summary>
    /// <param name="client">The client under test.</param>
    /// <param name="phase">The phase to pull in.</param>
    /// <param name="bandwidthLimitKib">The limit to pull with.</param>
    /// <returns>Every event, in order.</returns>
    private static async Task<List<AgentFilePullEvent>> DrainPullAsync(
        AgentTransferClient client,
        AgentPullPhase phase = AgentPullPhase.Initial,
        int bandwidthLimitKib = 0)
    {
        var request = new AgentFilePullRequest(Endpoint, Credential, "alice", "/home/alice", phase, bandwidthLimitKib, 4096);
        var collected = new List<AgentFilePullEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in client.PullAccountFilesAsync(request, timeout.Token))
        {
            collected.Add(item);
        }

        return collected;
    }

    /// <summary>Reads the whole database transfer stream.</summary>
    /// <param name="client">The client under test.</param>
    /// <returns>Every event, in order.</returns>
    private static async Task<List<AgentDatabaseTransferEvent>> DrainDatabaseAsync(AgentTransferClient client)
    {
        var request = new AgentDatabaseTransferRequest(Endpoint, Credential, "shop_wp", "alice", "alice_shop", 0);
        var collected = new List<AgentDatabaseTransferEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in client.StreamRemoteDatabaseAsync(request, timeout.Token))
        {
            collected.Add(item);
        }

        return collected;
    }
}
