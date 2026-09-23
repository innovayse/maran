using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Commands.InstallPhpVersion;

/// <summary>
/// Asks the agent to install a PHP version, watching the stream it answers with.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this closes.</b> The agent has been able to install a version on demand since plan 3,
/// and nothing drove it: <c>IAgentPhpClient.InstallVersionAsync</c> had no call site outside the
/// client, its resilience decorator and their tests. An operator whose customer needed 8.4 could
/// read that 8.4 was not installed and had no way to say so to the panel.
/// </para>
/// <para>
/// <b>Already installed is a SUCCESS, not a refusal.</b> Asking for a version that is present is
/// what an operator does when two people administer the same box, or when a page was left open —
/// and the state they asked for is the state they get. Refusing would make the panel argue about
/// something it agrees with. Nothing is installed twice: the list is read first, and the agent is
/// not asked at all.
/// </para>
/// <para>
/// <b>Why the whole stream is consumed rather than awaited as a unit.</b> Installing a runtime is
/// the package manager fetching and unpacking on whatever mirror the host has, which is minutes on
/// a bad day. The events carry a percentage and a stage line, and both go onto the task an operator
/// watches. A request that merely had not answered yet would leave them unable to tell a slow
/// download from a wedged one.
/// </para>
/// <para>
/// <b>The terminal event decides, never "the stream ended".</b> A stream that stops without a
/// terminal event has told us nothing about whether the packages landed, and reporting success on
/// its silence is exactly the mistake the backup runner's own remarks warn about. That case fails
/// the task with <see cref="ErrorMessages.PhpInstallOutcomeUnobserved"/>, which says what happened
/// rather than guessing at why.
/// </para>
/// </remarks>
public sealed class InstallPhpVersionCommandHandler
{
    /// <summary>The agent's PHP surface: lists what is installed, and installs.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Records the long-running task an operator watches.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Ties the task to the request that started it.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="correlationIds">Ties the task to its request.</param>
    /// <param name="journal">This module's audit journal.</param>
    public InstallPhpVersionCommandHandler(
        IAgentPhpClient php,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        SiteAuditJournal journal)
    {
        _php = php;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _journal = journal;
    }

    /// <summary>Installs the version, or reports why it could not be.</summary>
    /// <param name="command">The version to install, and who asked.</param>
    /// <param name="cancellationToken">Cancels the install.</param>
    /// <returns>The versions this server has after the attempt, or a typed failure.</returns>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> HandleAsync(
        InstallPhpVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var installed = await _php.ListVersionsAsync(cancellationToken);
        if (!installed.IsSuccess)
        {
            // The agent could not even be asked what is installed. Refused rather than attempted:
            // installing without knowing the current state is how a version gets installed twice.
            await _journal.RecordFailureAsync(
                AuditActions.PhpVersionInstalled,
                command.Version,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<IReadOnlyList<PhpVersionDto>>.Fail(installed.Error!);
        }

        var alreadyInstalled = installed.Value.Any(version =>
        {
            return string.Equals(version.Version, command.Version, StringComparison.Ordinal);
        });

        if (alreadyInstalled)
        {
            // Already there. Journalled as a success because it IS one from the caller's side — the
            // version they asked for is available — and with no task, because nothing ran.
            await _journal.RecordSuccessAsync(
                AuditActions.PhpVersionInstalled,
                command.Version,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<IReadOnlyList<PhpVersionDto>>.Ok(installed.Value);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.PhpVersionInstall, command.Version, _correlationIds.CorrelationId, cancellationToken);

        var outcome = await RunAsync(command.Version, taskId, cancellationToken);

        if (outcome is not null)
        {
            await _journal.RecordFailureAsync(
                AuditActions.PhpVersionInstalled,
                command.Version,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
            await _tasks.FailAsync(taskId, outcome, cancellationToken);

            return Result<IReadOnlyList<PhpVersionDto>>.Fail(Error.Of(outcome, ErrorType.Failure));
        }

        await _journal.RecordSuccessAsync(
            AuditActions.PhpVersionInstalled,
            command.Version,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
        await _tasks.CompleteAsync(taskId, cancellationToken);

        // Re-read rather than appending the requested version to the previous list: what this server
        // has is the agent's answer, and a list assembled from what we asked for would report a
        // version as present on the strength of the request that asked for it.
        var after = await _php.ListVersionsAsync(cancellationToken);

        return after.IsSuccess
            ? Result<IReadOnlyList<PhpVersionDto>>.Ok(after.Value)
            : Result<IReadOnlyList<PhpVersionDto>>.Fail(after.Error!);
    }

    /// <summary>Consumes the install stream, reporting progress, and names the failure if there is one.</summary>
    /// <param name="version">The version being installed.</param>
    /// <param name="taskId">The task to report progress against.</param>
    /// <param name="cancellationToken">Cancels the install.</param>
    /// <returns><see langword="null"/> when the version was installed; an error code otherwise.</returns>
    private async Task<string?> RunAsync(string version, Guid taskId, CancellationToken cancellationToken)
    {
        var terminal = false;
        string? failure = null;

        await foreach (var change in _php.InstallVersionAsync(version, cancellationToken))
        {
            switch (change.Kind)
            {
                case PhpInstallEventKind.Progress:
                    await _tasks.ReportAsync(taskId, (int)change.Percent, change.Stage, cancellationToken);
                    break;

                case PhpInstallEventKind.Installed:
                    terminal = true;
                    await _tasks.ReportAsync(taskId, 100, change.Stage, cancellationToken);
                    break;

                // Everything that is not progress and not success ends the attempt. They are listed
                // rather than folded into a default so that a new event kind on the wire arrives
                // here as a compiler-visible decision instead of being silently read as a failure.
                case PhpInstallEventKind.Failed:
                case PhpInstallEventKind.Dropped:
                case PhpInstallEventKind.Idle:
                case PhpInstallEventKind.Truncated:
                case PhpInstallEventKind.Cancelled:
                    terminal = true;
                    failure = change.ErrorCode ?? nameof(ErrorMessages.PhpInstallOutcomeUnobserved);
                    break;

                default:
                    terminal = true;
                    failure = nameof(ErrorMessages.PhpInstallOutcomeUnobserved);
                    break;
            }

            if (terminal)
            {
                break;
            }
        }

        // A stream that ended with no terminal event proves nothing about the packages. It is NOT
        // reported as a success, for the same reason the backup runner refuses to: "nothing threw"
        // is not an observation of an outcome.
        return terminal ? failure : nameof(ErrorMessages.PhpInstallOutcomeUnobserved);
    }
}
