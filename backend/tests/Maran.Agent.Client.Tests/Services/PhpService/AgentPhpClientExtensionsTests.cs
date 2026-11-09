using Grpc.Core;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using ClientAvailability = Maran.Agent.Client.Services.PhpService.PhpExtensionAvailability;
using WireAvailability = Maran.Agent.V1.PhpExtensionAvailability;

namespace Maran.Agent.Client.Tests.Services.PhpService;

/// <summary>Mapping contract of the extension rpcs of AgentPhpClient (listing → Result, switch stream → typed events).</summary>
public sealed class AgentPhpClientExtensionsTests
{
    /// <summary>How long any stream test may wait before it is a failure rather than a hang.</summary>
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The listing maps each wire extension with its state and availability, in the agent's order.</summary>
    [Fact]
    public async Task The_listing_maps_each_extension_with_its_state_and_availability_in_order()
    {
        var ok = new ListPhpExtensionsOk();
        ok.Extensions.Add(new PhpExtension { Name = "bcmath", Enabled = false, Availability = WireAvailability.Available });
        ok.Extensions.Add(new PhpExtension { Name = "curl", Enabled = true, Availability = WireAvailability.NotPackaged });
        ok.Extensions.Add(new PhpExtension
        {
            Name = "gd",
            Enabled = true,
            Availability = WireAvailability.DisableNotSupported,
        });
        var stub = new StubPhpService { ExtensionsResponse = new ListPhpExtensionsResponse { Ok = ok } };

        var result = await NewClient(stub).ListExtensionsAsync("8.3", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                new PhpExtensionDto("bcmath", false, ClientAvailability.Available),
                new PhpExtensionDto("curl", true, ClientAvailability.NotPackaged),
                new PhpExtensionDto("gd", true, ClientAvailability.DisableNotSupported),
            ],
            result.Value);
    }

    /// <summary>
    /// An unset wire availability, and one from an agent newer than the panel, read as unknown and never
    /// as an availability the agent did not state.
    /// </summary>
    [Fact]
    public async Task An_unset_or_unrecognised_availability_reads_as_unknown()
    {
        var ok = new ListPhpExtensionsOk();
        ok.Extensions.Add(new PhpExtension { Name = "gd", Enabled = true });
        ok.Extensions.Add(new PhpExtension { Name = "gmp", Enabled = true, Availability = (WireAvailability)99 });
        var stub = new StubPhpService { ExtensionsResponse = new ListPhpExtensionsResponse { Ok = ok } };

        var result = await NewClient(stub).ListExtensionsAsync("8.3", CancellationToken.None);

        Assert.Equal(ClientAvailability.Unknown, result.Value[0].Availability);
        Assert.Equal(ClientAvailability.Unknown, result.Value[1].Availability);
    }

    /// <summary>The listing sends the version it was given.</summary>
    [Fact]
    public async Task The_listing_sends_the_version_it_was_given()
    {
        var stub = new StubPhpService();

        await NewClient(stub).ListExtensionsAsync("8.2", CancellationToken.None);

        Assert.Equal("8.2", stub.LastListExtensionsRequest!.Version);
    }

    /// <summary>An error from the listing maps to a failed result with the agent's code and kind.</summary>
    [Fact]
    public async Task A_listing_error_maps_to_a_failed_result_with_the_agent_code_and_kind()
    {
        var stub = new StubPhpService
        {
            ExtensionsResponse = new ListPhpExtensionsResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "version not installed" },
            },
        };

        var result = await NewClient(stub).ListExtensionsAsync("8.3", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>The agent's own text and tool output are logged by the translator and never returned.</summary>
    [Fact]
    public async Task A_listing_error_logs_the_agents_text_and_never_returns_it()
    {
        var logger = new RecordingLogger<AgentPhpClient>();
        var stub = new StubPhpService
        {
            ExtensionsResponse = new ListPhpExtensionsResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.SystemFailure,
                    Message = "cannot run /usr/sbin/php-fpm8.3",
                    ToolOutput = "Segmentation fault in /usr/lib/php/20230831/gd.so",
                },
            },
        };

        var result = await NewClient(stub, logger).ListExtensionsAsync("8.3", CancellationToken.None);

        var returned = result.Error!.ToString();
        Assert.Equal("AgentSystemFailure", result.Error.Code);
        Assert.DoesNotContain("/usr/sbin", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("gd.so", returned, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/usr/sbin/php-fpm8.3", logged, StringComparison.Ordinal);
        Assert.Contains("gd.so", logged, StringComparison.Ordinal);
    }

    /// <summary>An unset listing oneof maps to the invalid response error.</summary>
    [Fact]
    public async Task An_unset_listing_oneof_maps_to_the_invalid_response_error()
    {
        var result = await NewClient(new StubPhpService()).ListExtensionsAsync("8.3", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Progress then success ends the stream with a changed event echoing the new state.</summary>
    [Fact]
    public async Task Progress_then_success_ends_the_stream_with_a_changed_event_echoing_the_new_state()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Progress = new Progress { Percent = 40, Stage = "install" },
        });
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Ok = new SetPhpExtensionOk { Version = "8.3", Extension = "gd", Enabled = true },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(2, events.Count);
        Assert.Equal(PhpExtensionEventKind.Progress, events[0].Kind);
        Assert.Equal(40u, events[0].Percent);
        Assert.Equal("install", events[0].Stage);
        Assert.Equal(PhpExtensionEventKind.Changed, events[1].Kind);
        Assert.Equal("8.3", events[1].Version);
        Assert.Equal("gd", events[1].Extension);
        Assert.True(events[1].Enabled);
        Assert.Null(events[1].Error);
    }

    /// <summary>The switch sends the version, the extension and the requested state it was given.</summary>
    [Fact]
    public async Task The_switch_sends_the_version_the_extension_and_the_state_it_was_given()
    {
        var stub = new StubPhpService();

        await CollectAsync(stub);

        Assert.Equal("8.3", stub.LastSetExtensionRequest!.Version);
        Assert.Equal("gd", stub.LastSetExtensionRequest.Extension);
        Assert.True(stub.LastSetExtensionRequest.Enabled);
    }

    /// <summary>A stream that stops without an outcome ends with a truncated event, never a success.</summary>
    [Fact]
    public async Task A_stream_that_stops_without_an_outcome_ends_with_a_truncated_event()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Progress = new Progress { Percent = 40, Stage = "install" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(2, events.Count);
        Assert.Equal(PhpExtensionEventKind.Truncated, events[1].Kind);
    }

    /// <summary>A stream the agent dropped ends with a dropped event.</summary>
    [Fact]
    public async Task A_stream_the_agent_dropped_ends_with_a_dropped_event()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamDropped, Message = "client stopped reading" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(PhpExtensionEventKind.Dropped, Assert.Single(events).Kind);
    }

    /// <summary>A stream the agent closed for idleness ends with an idle event.</summary>
    [Fact]
    public async Task A_stream_the_agent_closed_for_idleness_ends_with_an_idle_event()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamIdle, Message = "no output" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(PhpExtensionEventKind.Idle, Assert.Single(events).Kind);
    }

    /// <summary>A stream that failed ends with a failed event carrying the typed code and kind.</summary>
    [Fact]
    public async Task A_stream_that_failed_ends_with_a_failed_event_carrying_the_typed_code_and_kind()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "extension not in the catalogue" },
        });

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(PhpExtensionEventKind.Failed, last.Kind);
        Assert.Equal("AgentInvalidInput", last.Error!.Code);
        Assert.Equal(ErrorType.Validation, last.Error.Type);
    }

    /// <summary>A failure carries the agent's classification, and its text is logged and never returned.</summary>
    [Fact]
    public async Task A_failed_event_logs_the_agents_text_and_never_carries_it()
    {
        var logger = new RecordingLogger<AgentPhpClient>();
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse
        {
            Error = new AgentError
            {
                Code = ErrorCode.SystemFailure,
                Message = "phpenmod failed",
                ToolOutput = "ERROR: module gd ini file /etc/php/8.3/mods-available/gd.ini does not exist",
            },
        });

        var events = await CollectAsync(stub, logger);

        var last = Assert.Single(events);
        Assert.Equal("AgentSystemFailure", last.Error!.Code);
        Assert.DoesNotContain("/etc/php", last.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("/etc/php/8.3/mods-available/gd.ini", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    /// <summary>A message carrying no branch ends with a failed event naming an invalid response.</summary>
    [Fact]
    public async Task A_message_carrying_no_branch_ends_with_a_failed_event()
    {
        var stub = new StubPhpService();
        stub.ExtensionResponses.Add(new SetPhpExtensionResponse());

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(PhpExtensionEventKind.Failed, last.Kind);
        Assert.Equal("AgentInvalidResponse", last.Error!.Code);
        Assert.Equal(ErrorType.Failure, last.Error.Type);
    }

    /// <summary>A transport that failed ends the stream with a failed event naming an unavailable agent, not an exception.</summary>
    [Fact]
    public async Task A_transport_failure_ends_the_stream_with_a_failed_event_not_an_exception()
    {
        var stub = new StubPhpService
        {
            ExtensionStreamFailure = new RpcException(new Status(StatusCode.Unavailable, "socket closed")),
        };

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(PhpExtensionEventKind.Failed, last.Kind);
        Assert.Equal("AgentUnavailable", last.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, last.Error.Type);
    }

    /// <summary>A cancelled stream ends with a cancelled event and stops pulling.</summary>
    [Fact]
    public async Task A_cancelled_stream_ends_with_a_cancelled_event_and_stops_pulling()
    {
        var stub = new StubPhpService();
        for (var index = 0; index < 50; index++)
        {
            stub.ExtensionResponses.Add(new SetPhpExtensionResponse
            {
                Progress = new Progress { Percent = 1, Stage = "install" },
            });
        }

        using var cancellation = new CancellationTokenSource();
        var client = NewClient(stub);

        async Task<List<PhpExtensionEvent>> DrainAsync()
        {
            var events = new List<PhpExtensionEvent>();
            await foreach (var item in client.SetExtensionAsync("8.3", "gd", true, cancellation.Token))
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

        Assert.Equal(PhpExtensionEventKind.Cancelled, collected[^1].Kind);
        Assert.DoesNotContain(collected, item =>
        {
            return item.Kind == PhpExtensionEventKind.Truncated;
        });
        Assert.True(
            stub.ExtensionYieldedCount < 10,
            $"kept pulling after cancellation: {stub.ExtensionYieldedCount} messages");
    }

    /// <summary>Builds a client over the stub with a logger that discards its output.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentPhpClient NewClient(StubPhpService stub)
    {
        return NewClient(stub, NullLogger<AgentPhpClient>.Instance);
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
    /// Drains the switch stream under a hard deadline, so a stream that never ends fails the test
    /// instead of hanging the run.
    /// </summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>Every event the client produced, in order.</returns>
    private static async Task<List<PhpExtensionEvent>> CollectAsync(StubPhpService stub)
    {
        return await CollectAsync(stub, NullLogger<AgentPhpClient>.Instance);
    }

    /// <summary>
    /// Drains the switch stream under a hard deadline, so a stream that never ends fails the test
    /// instead of hanging the run.
    /// </summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>Every event the client produced, in order.</returns>
    private static async Task<List<PhpExtensionEvent>> CollectAsync(
        StubPhpService stub,
        Microsoft.Extensions.Logging.ILogger<AgentPhpClient> logger)
    {
        var client = NewClient(stub, logger);

        async Task<List<PhpExtensionEvent>> DrainAsync()
        {
            var events = new List<PhpExtensionEvent>();
            await foreach (var item in client.SetExtensionAsync("8.3", "gd", true, CancellationToken.None))
            {
                events.Add(item);
            }

            return events;
        }

        return await DrainAsync().WaitAsync(StreamTimeout);
    }
}
