using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Errors;

/// <summary>
/// A stream whose transport fails ends in a typed terminal event, not in the raw exception (issue
/// #87), and the guard that does it never hides a cancellation or a defect.
/// </summary>
public sealed class AgentStreamGuardTests
{
    /// <summary>Deadline for any call to a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Events before the failure are forwarded, then one terminal event ends the sequence.</summary>
    [Fact]
    public async Task A_transport_failure_after_some_events_ends_the_sequence_in_one_terminal_event()
    {
        var events = new List<string>();

        await foreach (var item in AgentStreamGuard.EndOnTransportFailureAsync(
            FailingAfter("a", "b", new RpcException(new Status(StatusCode.Unavailable, "gone"))),
            exception =>
            {
                return $"terminal:{exception.StatusCode}";
            },
            CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal(["a", "b", "terminal:Unavailable"], events);
    }

    /// <summary>A status raised after the caller's token fired is the caller leaving, and is not mapped.</summary>
    [Fact]
    public async Task A_status_raised_after_the_callers_token_fired_is_not_mapped()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var item in AgentStreamGuard.EndOnTransportFailureAsync(
                FailingAfter("a", "b", new RpcException(new Status(StatusCode.Cancelled, "x"))),
                _ =>
                {
                    return "terminal";
                },
                cancellation.Token))
            {
                Assert.NotNull(item);
            }
        });
    }

    /// <summary>A cancellation the caller did not ask for is the transport giving up, and ends in the terminal event.</summary>
    [Fact]
    public async Task A_cancellation_the_caller_did_not_ask_for_ends_in_the_terminal_event()
    {
        var events = new List<string>();

        await foreach (var item in AgentStreamGuard.EndOnTransportFailureAsync(
            FailingAfter("a", "b", new OperationCanceledException()),
            _ =>
            {
                return "terminal";
            },
            CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal(["a", "b", "terminal"], events);
    }

    /// <summary>A cancellation the caller asked for is not mapped.</summary>
    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_not_mapped()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in AgentStreamGuard.EndOnTransportFailureAsync(
                FailingAfter("a", "b", new OperationCanceledException()),
                _ =>
                {
                    return "terminal";
                },
                cancellation.Token))
            {
                Assert.NotNull(item);
            }
        });
    }

    /// <summary>A defect that is not a transport status stays loud.</summary>
    [Fact]
    public async Task An_exception_that_is_not_a_transport_status_is_not_swallowed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in AgentStreamGuard.EndOnTransportFailureAsync(
                FailingAfter("a", "b", new InvalidOperationException("defect")),
                _ =>
                {
                    return "terminal";
                },
                CancellationToken.None))
            {
                Assert.NotNull(item);
            }
        });
    }

    /// <summary>A create stream on an agent that does not exist ends in a failed event with the typed code.</summary>
    [Fact]
    public async Task A_backup_create_against_a_missing_agent_ends_in_a_failed_event()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentBackupClient(channel, NullLogger<AgentBackupClient>.Instance);
        var destination = new AgentBackupDestination(
            AgentBackupDestinationKind.Local,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SensitiveString(string.Empty),
            new SensitiveString(string.Empty),
            false);
        var events = new List<BackupCreateEvent>();

        using var timeout = new CancellationTokenSource(TestTimeout);

        await foreach (var item in client.CreateAsync("alice", "b1", destination, timeout.Token))
        {
            events.Add(item);
        }

        var terminal = Assert.Single(events);
        Assert.Equal(BackupCreateEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentUnavailable", terminal.ErrorCode);
    }

    /// <summary>A log tail on an agent that does not exist ends in a failed event with the typed code.</summary>
    [Fact]
    public async Task A_log_tail_against_a_missing_agent_ends_in_a_failed_event()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentSitesClient(channel, NullLogger<AgentSitesClient>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var events = new List<SiteLogEvent>();

        await foreach (var item in client.TailLogAsync("alice", "a.example.com", SiteLogSource.Access, 10, timeout.Token))
        {
            events.Add(item);
        }

        var terminal = Assert.Single(events);
        Assert.Equal(SiteLogEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentUnavailable", terminal.ErrorCode);
    }

    /// <summary>Yields two items and then throws, like a stream torn mid-flight.</summary>
    /// <param name="first">The first item.</param>
    /// <param name="second">The second item.</param>
    /// <param name="failure">What the stream throws after them.</param>
    /// <returns>The sequence.</returns>
    private static async IAsyncEnumerable<string> FailingAfter(
        string first,
        string second,
        Exception failure)
    {
        await Task.Yield();
        yield return first;
        yield return second;

        throw failure;
    }
}
