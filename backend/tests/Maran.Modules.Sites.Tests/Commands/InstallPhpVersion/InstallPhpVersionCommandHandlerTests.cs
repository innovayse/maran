using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Commands.InstallPhpVersion;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Commands.InstallPhpVersion;

/// <summary>
/// Proves what the install does with each shape of answer the agent can give it.
/// </summary>
/// <remarks>
/// The cases worth naming are the two that look like success and are not: a stream that ends with
/// no terminal event at all, and a terminal event that is neither progress nor "installed". Both
/// must fail, because neither is an observation that the packages landed — "nothing threw" is not
/// an outcome (rules/testing.md).
/// </remarks>
public sealed class InstallPhpVersionCommandHandlerTests
{
    /// <summary>A version asked for that the host already has does not reach the agent's installer.</summary>
    [Fact]
    public async Task An_already_installed_version_is_a_success_and_nothing_is_installed_twice()
    {
        var php = new RecordingAgentPhpClient("8.3", "8.4");
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();
        var handler = Handler(php, tasks, audit);

        var result = await handler.HandleAsync(Command("8.4"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, version => { return version.Version == "8.4"; });
        // The agent was never asked to install, and no task was begun: nothing ran, so there is
        // nothing for an operator to watch.
        Assert.Empty(php.InstallCalls);
        Assert.Empty(tasks.Tasks);
        Assert.True(audit.Entries.Single().Succeeded);
    }

    /// <summary>A version the host lacks is installed, watched, and read back from the agent.</summary>
    [Fact]
    public async Task A_missing_version_is_installed_and_its_progress_is_reported()
    {
        var php = new RecordingAgentPhpClient("8.3");
        php.StageInstall(
            [
                new PhpInstallEvent(PhpInstallEventKind.Progress, 40, "fetching packages", "8.4", null),
                new PhpInstallEvent(PhpInstallEventKind.Installed, 100, "installed", "8.4", null),
            ],
            thenInstalled: "8.4");
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();
        var handler = Handler(php, tasks, audit);

        var result = await handler.HandleAsync(Command("8.4"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, version => { return version.Version == "8.4"; });
        Assert.Equal(["8.4"], php.InstallCalls);

        var task = tasks.Tasks.Single();
        Assert.Equal(TaskKinds.PhpVersionInstall, task.Kind);
        // The subject is the version, because that is the only thing this operation acts on.
        Assert.Equal("8.4", task.Subject);
        Assert.True(task.Completed);
        Assert.Contains(task.Reports, report => { return report.Percent == 40; });
        Assert.True(audit.Entries.Single().Succeeded);
    }

    /// <summary>A failure the agent names is the failure the task and the caller are given.</summary>
    [Fact]
    public async Task A_named_failure_fails_the_task_with_the_agents_own_code()
    {
        var php = new RecordingAgentPhpClient("8.3");
        php.StageInstall(
            [
                new PhpInstallEvent(PhpInstallEventKind.Progress, 10, "fetching packages", "8.4", null),
                new PhpInstallEvent(PhpInstallEventKind.Failed, 10, "package manager refused", "8.4", "PhpRepositoryUnavailable"),
            ]);
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();
        var handler = Handler(php, tasks, audit);

        var result = await handler.HandleAsync(Command("8.4"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpRepositoryUnavailable", result.Error!.Code);
        Assert.Equal("PhpRepositoryUnavailable", tasks.Tasks.Single().FailureCode);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>
    /// A stream that ends without a terminal event is a FAILURE, not a quiet success.
    /// </summary>
    /// <remarks>
    /// This is the case the handler exists to get right. The agent said nothing about whether the
    /// packages landed; reporting success on that silence would tell an operator a version is
    /// available when the only honest answer is that nobody knows.
    /// </remarks>
    [Fact]
    public async Task A_stream_that_ends_with_no_terminal_event_is_not_reported_as_installed()
    {
        var php = new RecordingAgentPhpClient("8.3");
        php.StageInstall(
            [
                new PhpInstallEvent(PhpInstallEventKind.Progress, 70, "unpacking", "8.4", null),
            ]);
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();
        var handler = Handler(php, tasks, audit);

        var result = await handler.HandleAsync(Command("8.4"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PhpInstallOutcomeUnobserved", result.Error!.Code);
        Assert.Equal("PhpInstallOutcomeUnobserved", tasks.Tasks.Single().FailureCode);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>An agent that cannot say what is installed is not asked to install.</summary>
    /// <remarks>
    /// Refusing rather than attempting is the point: installing without knowing the current state
    /// is how a version gets installed twice, and the second attempt is the one that surprises an
    /// operator who is watching packages they did not expect to move.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_installed_list_refuses_before_anything_is_installed()
    {
        var php = new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Failure));
        var tasks = new RecordingTaskRecorder();
        var audit = new RecordingAuditWriter();
        var handler = Handler(php, tasks, audit);

        var result = await handler.HandleAsync(Command("8.4"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(php.InstallCalls);
        Assert.Empty(tasks.Tasks);
        Assert.False(audit.Entries.Single().Succeeded);
    }

    /// <summary>Builds the handler over the given doubles.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="audit">Receives this module's journal entries.</param>
    /// <returns>The handler under test.</returns>
    private static InstallPhpVersionCommandHandler Handler(
        RecordingAgentPhpClient php,
        RecordingTaskRecorder tasks,
        RecordingAuditWriter audit)
    {
        return new InstallPhpVersionCommandHandler(
            php,
            tasks,
            new StubCorrelationIdAccessor("correlation-1"),
            new SiteAuditJournal(audit, FakeCurrentUser.Admin()));
    }

    /// <summary>Builds the command for a version.</summary>
    /// <param name="version">The version to install.</param>
    /// <returns>The command, stamped as an administrator's request.</returns>
    private static InstallPhpVersionCommand Command(string version)
    {
        return new InstallPhpVersionCommand(version, "203.0.113.7", "tests");
    }
}
