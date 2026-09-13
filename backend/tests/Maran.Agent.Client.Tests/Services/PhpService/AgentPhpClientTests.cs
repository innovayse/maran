using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.PhpService;

/// <summary>Mapping contract of AgentPhpClient (proto oneof → Result, and stream → typed events).</summary>
public sealed class AgentPhpClientTests
{
    /// <summary>How long any stream test may wait before it is a failure rather than a hang.</summary>
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Ok payload maps to success result.</summary>
    [Fact]
    public async Task Ok_payload_maps_to_success_result()
    {
        var ok = new ListPhpVersionsOk();
        ok.Versions.Add(new PhpVersion { Version = "8.3", FpmSocketDirectory = "/run/php" });
        var stub = new StubPhpService { ListResponse = new ListPhpVersionsResponse { Ok = ok } };

        var result = await NewClient(stub, NullLogger<AgentPhpClient>.Instance).ListVersionsAsync(
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.3", Assert.Single(result.Value).Version);
    }

    /// <summary>Error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubPhpService
        {
            ListResponse = new ListPhpVersionsResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "dpkg is locked" },
            },
        };

        var result = await NewClient(stub, NullLogger<AgentPhpClient>.Instance).ListVersionsAsync(
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>Unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Unset_oneof_maps_to_invalid_response_error()
    {
        var result = await NewClient(new StubPhpService(), NullLogger<AgentPhpClient>.Instance).ListVersionsAsync(
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>An unset default flag stays unknown rather than becoming false.</summary>
    [Fact]
    public async Task An_unset_default_flag_stays_unknown_rather_than_becoming_false()
    {
        var ok = new ListPhpVersionsOk();
        ok.Versions.Add(new PhpVersion { Version = "8.3", FpmSocketDirectory = "/run/php" });
        ok.Versions.Add(new PhpVersion { Version = "8.2", FpmSocketDirectory = "/run/php", IsDefault = false });
        ok.Versions.Add(new PhpVersion { Version = "8.1", FpmSocketDirectory = "/run/php", IsDefault = true });
        var stub = new StubPhpService { ListResponse = new ListPhpVersionsResponse { Ok = ok } };

        var result = await NewClient(stub, NullLogger<AgentPhpClient>.Instance).ListVersionsAsync(
            CancellationToken.None);

        Assert.Null(result.Value[0].IsDefault);
        Assert.False(result.Value[1].IsDefault);
        Assert.True(result.Value[2].IsDefault);
    }

    /// <summary>The agents message and tool output are logged and never returned.</summary>
    [Fact]
    public async Task The_agents_message_and_tool_output_are_logged_and_never_returned()
    {
        var logger = new RecordingLogger<AgentPhpClient>();
        var stub = new StubPhpService
        {
            ListResponse = new ListPhpVersionsResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.SystemFailure,
                    Message = "cannot read /etc/php/8.3/fpm/pool.d",
                    ToolOutput = "dpkg: error: /var/lib/dpkg/lock-frontend is locked",
                },
            },
        };

        var result = await NewClient(stub, logger).ListVersionsAsync(CancellationToken.None);

        var returned = result.Error!.ToString();
        Assert.Equal("AgentSystemFailure", result.Error.Code);
        Assert.DoesNotContain("/etc/php", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/lib/dpkg", returned, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/etc/php/8.3/fpm/pool.d", logged, StringComparison.Ordinal);
        Assert.Contains("/var/lib/dpkg/lock-frontend", logged, StringComparison.Ordinal);
    }

    /// <summary>Progress then success ends the install stream with an installed event.</summary>
    [Fact]
    public async Task Progress_then_success_ends_the_install_stream_with_an_installed_event()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Progress = new Progress { Percent = 40, Stage = "downloading" },
        });
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Ok = new InstallPhpVersionOk { Version = "8.4" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(2, events.Count);
        Assert.Equal(PhpInstallEventKind.Progress, events[0].Kind);
        Assert.Equal(40u, events[0].Percent);
        Assert.Equal("downloading", events[0].Stage);
        Assert.Equal(PhpInstallEventKind.Installed, events[1].Kind);
        Assert.Equal("8.4", events[1].Version);
    }

    /// <summary>An install stream that stops without an outcome ends with a truncated event.</summary>
    [Fact]
    public async Task An_install_stream_that_stops_without_an_outcome_ends_with_a_truncated_event()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Progress = new Progress { Percent = 40, Stage = "downloading" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(2, events.Count);
        Assert.Equal(PhpInstallEventKind.Truncated, events[1].Kind);
    }

    /// <summary>An install stream the agent dropped ends with a dropped event.</summary>
    [Fact]
    public async Task An_install_stream_the_agent_dropped_ends_with_a_dropped_event()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamDropped, Message = "client stopped reading" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(PhpInstallEventKind.Dropped, Assert.Single(events).Kind);
    }

    /// <summary>An install stream the agent closed for idleness ends with an idle event.</summary>
    [Fact]
    public async Task An_install_stream_the_agent_closed_for_idleness_ends_with_an_idle_event()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamIdle, Message = "no output" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(PhpInstallEventKind.Idle, Assert.Single(events).Kind);
    }

    /// <summary>An install stream that failed ends with a failed event carrying the typed code.</summary>
    [Fact]
    public async Task An_install_stream_that_failed_ends_with_a_failed_event_carrying_the_typed_code()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse
        {
            Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "unsupported version" },
        });

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(PhpInstallEventKind.Failed, last.Kind);
        Assert.Equal("AgentInvalidInput", last.ErrorCode);
    }

    /// <summary>An install message carrying no branch ends with a failed event.</summary>
    [Fact]
    public async Task An_install_message_carrying_no_branch_ends_with_a_failed_event()
    {
        var stub = new StubPhpService();
        stub.InstallResponses.Add(new InstallPhpVersionResponse());

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(PhpInstallEventKind.Failed, last.Kind);
        Assert.Equal("AgentInvalidResponse", last.ErrorCode);
    }

    /// <summary>Install sends the version it was given.</summary>
    [Fact]
    public async Task Install_sends_the_version_it_was_given()
    {
        var stub = new StubPhpService();

        await CollectAsync(stub);

        Assert.Equal("8.4", stub.LastInstallRequest!.Version);
    }

    /// <summary>The socket directory of every listed version is carried through.</summary>
    [Fact]
    public async Task The_socket_directory_of_every_listed_version_is_carried_through()
    {
        var ok = new ListPhpVersionsOk();
        ok.Versions.Add(new PhpVersion { Version = "8.3", FpmSocketDirectory = "/run/php/8.3" });
        ok.Versions.Add(new PhpVersion { Version = "8.2", FpmSocketDirectory = "/run/php/8.2" });
        var stub = new StubPhpService { ListResponse = new ListPhpVersionsResponse { Ok = ok } };

        var result = await NewClient(stub, NullLogger<AgentPhpClient>.Instance).ListVersionsAsync(
            CancellationToken.None);

        Assert.Equal("/run/php/8.3", result.Value[0].FpmSocketDirectory);
        Assert.Equal("/run/php/8.2", result.Value[1].FpmSocketDirectory);
    }

    /// <summary>A cancelled install ends with a cancelled event and stops pulling.</summary>
    [Fact]
    public async Task A_cancelled_install_ends_with_a_cancelled_event_and_stops_pulling()
    {
        var stub = new StubPhpService();
        for (var index = 0; index < 50; index++)
        {
            stub.InstallResponses.Add(new InstallPhpVersionResponse
            {
                Progress = new Progress { Percent = 1, Stage = "downloading" },
            });
        }

        using var cancellation = new CancellationTokenSource();
        var client = NewClient(stub, NullLogger<AgentPhpClient>.Instance);

        async Task<List<PhpInstallEvent>> DrainAsync()
        {
            var events = new List<PhpInstallEvent>();
            await foreach (var item in client.InstallVersionAsync("8.4", cancellation.Token))
            {
                events.Add(item);
                if (events.Count == 3)
                {
                    await cancellation.CancelAsync();
                }
            }

            return events;
        }

        var collected = await DrainAsync().WaitAsync(StreamTimeout);

        Assert.Equal(PhpInstallEventKind.Cancelled, collected[^1].Kind);
        Assert.DoesNotContain(collected, item =>
        {
            return item.Kind == PhpInstallEventKind.Truncated;
        });
        Assert.True(
            stub.InstallYieldedCount < 10,
            $"kept pulling after cancellation: {stub.InstallYieldedCount} messages");
    }

    /// <summary>Builds a client over the stub.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>The client under test.</returns>
    private static AgentPhpClient NewClient(
        StubPhpService stub,
        Microsoft.Extensions.Logging.ILogger<AgentPhpClient> logger)
    {
        return new AgentPhpClient(stub, logger);
    }

    /// <summary>
    /// Drains the install stream under a hard deadline, so a stream that never ends fails the test
    /// instead of hanging the run.
    /// </summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>Every event the client produced, in order.</returns>
    private static async Task<List<PhpInstallEvent>> CollectAsync(StubPhpService stub)
    {
        var client = NewClient(stub, NullLogger<AgentPhpClient>.Instance);

        async Task<List<PhpInstallEvent>> DrainAsync()
        {
            var events = new List<PhpInstallEvent>();
            await foreach (var item in client.InstallVersionAsync("8.4", CancellationToken.None))
            {
                events.Add(item);
            }

            return events;
        }

        return await DrainAsync().WaitAsync(StreamTimeout);
    }
}
