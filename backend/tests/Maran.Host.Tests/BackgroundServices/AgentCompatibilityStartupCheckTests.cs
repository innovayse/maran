using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.BackgroundServices;
using Maran.Host.Tests.Fixtures;
using Maran.Host.Tests.HealthChecks;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Host.Tests.BackgroundServices;

/// <summary>
/// The startup check states, once, whether the agent speaks a contract revision this panel drives, and
/// never stops the host over the answer: an incompatible or absent agent is logged and the check returns.
/// </summary>
/// <remarks>
/// The check is driven through its public <see cref="AgentCompatibilityStartupCheck.CheckOnceAsync"/>
/// over the real probe and the real probe pipeline, with only the client standing in for the agent.
/// </remarks>
public sealed class AgentCompatibilityStartupCheckTests
{
    /// <summary>Deadline for a check, so a runaway retry fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// An agent outside the window is logged once at Error, naming both ends of the window, and the
    /// check returns normally.
    /// </summary>
    [Fact]
    public async Task The_check_logs_an_error_naming_both_ends_of_the_window_for_an_incompatible_agent()
    {
        var logger = new LogEntryRecorder<AgentCompatibilityStartupCheck>();
        var check = NewCheck(Incompatible(), logger);

        await check.CheckOnceAsync(CancellationToken.None).WaitAsync(TestTimeout);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains($"{AgentProtoVersionWindow.Minimum}..{AgentProtoVersionWindow.Maximum}", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>An incompatible agent does not make the check throw: the host keeps starting.</summary>
    [Fact]
    public async Task The_check_returns_normally_for_an_incompatible_agent()
    {
        var check = NewCheck(Incompatible(), new LogEntryRecorder<AgentCompatibilityStartupCheck>());

        var completed = check.CheckOnceAsync(CancellationToken.None);

        await completed.WaitAsync(TestTimeout);
        Assert.True(completed.IsCompletedSuccessfully);
    }

    /// <summary>A compatible agent is logged at Information and produces no warning or error.</summary>
    [Fact]
    public async Task The_check_with_a_compatible_agent_logs_no_error()
    {
        var logger = new LogEntryRecorder<AgentCompatibilityStartupCheck>();
        var check = NewCheck(new RecordingAgentSystemClient(), logger);

        await check.CheckOnceAsync(CancellationToken.None).WaitAsync(TestTimeout);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>An unreachable agent is logged at Warning, not Error, and the check returns.</summary>
    [Fact]
    public async Task The_check_with_an_unreachable_agent_logs_a_warning_not_an_error_and_returns()
    {
        var logger = new LogEntryRecorder<AgentCompatibilityStartupCheck>();
        var check = NewCheck(new RecordingAgentSystemClient { FailuresBeforeSuccess = int.MaxValue }, logger);

        await check.CheckOnceAsync(CancellationToken.None).WaitAsync(TestTimeout);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    /// <summary>A defect inside the probe is logged by the check's own guard and never escapes it.</summary>
    [Fact]
    public async Task A_defect_inside_the_probe_is_logged_and_never_escapes_the_check()
    {
        var logger = new LogEntryRecorder<AgentCompatibilityStartupCheck>();
        var failure = new InvalidOperationException("a defect, not a transport failure");
        var check = NewCheck(new StubAgentSystemClient(failure), logger);

        await check.CheckOnceAsync(CancellationToken.None).WaitAsync(TestTimeout);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
    }

    /// <summary>The host's own cancellation is not a finding about the agent: it is rethrown, not logged.</summary>
    [Fact]
    public async Task The_hosts_own_cancellation_is_rethrown_from_the_check()
    {
        var logger = new LogEntryRecorder<AgentCompatibilityStartupCheck>();
        var check = NewCheck(StubAgentSystemClient.ThatNeverAnswers(), logger);
        using var cancelled = new CancellationTokenSource();

        // The agent never answers, so the check is in flight when the host stops.
        var running = check.CheckOnceAsync(cancelled.Token);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await running.WaitAsync(TestTimeout);
        });

        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// Starting the service hands control back to the host before any agent call can block the startup
    /// path, even when the first call does its work before its first await.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_hands_control_back_before_any_agent_call()
    {
        using var client = new BlockingAgentSystemClient();
        var check = NewCheck(client, new LogEntryRecorder<AgentCompatibilityStartupCheck>());

        try
        {
            // Off the test thread, so a start that blocks fails on the timeout instead of hanging.
            var start = Task.Run(async () =>
            {
                await check.StartAsync(CancellationToken.None);
            });

            await start.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            client.Release();
            await check.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Builds the check around a client and a recording logger.</summary>
    /// <param name="client">The client standing in for the agent.</param>
    /// <param name="logger">The logger the check writes to.</param>
    /// <returns>The check under test.</returns>
    private static AgentCompatibilityStartupCheck NewCheck(
        IAgentSystemClient client,
        ILogger<AgentCompatibilityStartupCheck> logger)
    {
        return new AgentCompatibilityStartupCheck(AgentHealthProbeFactory.Create(client), logger);
    }

    /// <summary>A client whose handshake the window refused.</summary>
    /// <returns>The stub.</returns>
    private static StubAgentSystemClient Incompatible()
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable)));
    }
}
