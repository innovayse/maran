using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using AgentPhpExtensionDto = Maran.Agent.Client.Services.PhpService.PhpExtensionDto;
using PhpExtensionDto = Maran.Modules.Sites.Common.PhpExtensionDto;

namespace Maran.Modules.Sites.Commands.SetPhpExtension;

/// <summary>
/// Asks the agent to switch one catalogue PHP extension on or off for one version, watching the
/// stream it answers with, and answers the version's extensions as they are afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same shape as installing a version, and for the same reasons.</b> The change is minutes on a
/// bad day when it installs a package, and ends in a php-fpm validation and reload that reaches every
/// account's pool of the version; the events carry a percentage and a stage line that go onto the task
/// an administrator watches. The terminal event decides, never "the stream ended": a stream that stops
/// without one fails the task with <see cref="ErrorMessages.PhpExtensionToggleFailed"/>.
/// </para>
/// <para>
/// <b>Already in the requested state is a SUCCESS and runs nothing.</b> The list is read first; an
/// extension that is already as asked is reported as done, journalled, and the agent is not asked to
/// change anything. A request the host's OS family cannot honour (no package to install, or no switch
/// to turn it off) is refused here with <see cref="ErrorMessages.PhpExtensionNotAvailable"/> before a
/// task is begun, from the availability the agent itself reported.
/// </para>
/// <para>
/// <b>The state is observed, not assumed.</b> After a terminal "changed" the list is read again and
/// the extension must be in the requested state; a stream claiming success over an extension that is
/// not as asked fails the task. Administrator-only is enforced by the controller's policy; this handler
/// is reached by nothing a customer can send.
/// </para>
/// </remarks>
public sealed class SetPhpExtensionCommandHandler
{
    /// <summary>
    /// The failure of a change whose outcome was never stated, or whose result did not match what was
    /// asked: a fault on the panel's side of the conversation, never a verdict on the packages.
    /// </summary>
    private static readonly Error ToggleFailed =
        Error.Of(nameof(ErrorMessages.PhpExtensionToggleFailed), ErrorType.Failure);

    /// <summary>The agent's PHP surface: lists a version's extensions, and switches one.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Records the long-running task an administrator watches.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Ties the task to the request that started it.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Resolves the localized extension labels of the answer.</summary>
    private readonly SiteDisplayNames _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="correlationIds">Ties the task to its request.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Resolves the localized extension labels of the answer.</param>
    public SetPhpExtensionCommandHandler(
        IAgentPhpClient php,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        SiteAuditJournal journal,
        SiteDisplayNames displayNames)
    {
        _php = php;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Switches the extension, or reports why it could not be.</summary>
    /// <param name="command">The version, the extension, the state to put it in, and who asked.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>
    /// The version's extensions after the attempt, for an administrator (availability included), or a
    /// typed failure.
    /// </returns>
    public async Task<Result<IReadOnlyList<PhpExtensionDto>>> HandleAsync(
        SetPhpExtensionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await _php.ListExtensionsAsync(command.Version, cancellationToken);
        if (!current.IsSuccess)
        {
            // The agent could not say what the version loads, or does not have the version: refused
            // rather than attempted, because switching without knowing the state is how a change is
            // made twice.
            return await RefuseAsync(command, current.Error!, cancellationToken);
        }

        var entry = current.Value.FirstOrDefault(extension =>
        {
            return string.Equals(extension.Name, command.Extension, StringComparison.Ordinal);
        });
        if (entry is null)
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.PhpExtensionNotAllowed), ErrorType.Validation),
                cancellationToken);
        }

        if (entry.Enabled == command.Enabled)
        {
            // Already as asked. A success from the caller's side, with no task, because nothing ran.
            await RecordAsync(command, succeeded: true, cancellationToken);

            return Result<IReadOnlyList<PhpExtensionDto>>.Ok(Describe(current.Value));
        }

        if (!CanBeSwitched(entry.Availability, command.Enabled))
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.PhpExtensionNotAvailable), ErrorType.Validation),
                cancellationToken);
        }

        // No account and audience AdministratorOnly: an extension is switched for every account of the
        // version, so the task concerns no account and is no customer's to read.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.PhpExtensionChange,
            $"{command.Version} {command.Extension}",
            accountId: null,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            cancellationToken);

        var failure = await RunAsync(command, taskId, cancellationToken);
        IReadOnlyList<AgentPhpExtensionDto>? after = null;
        if (failure is null)
        {
            (failure, after) = await ObserveAsync(command, cancellationToken);
        }

        if (failure is not null)
        {
            await RecordAsync(command, succeeded: false, cancellationToken);
            await _tasks.FailAsync(taskId, failure.Code, cancellationToken);

            return Result<IReadOnlyList<PhpExtensionDto>>.Fail(failure);
        }

        await RecordAsync(command, succeeded: true, cancellationToken);
        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<IReadOnlyList<PhpExtensionDto>>.Ok(Describe(after!));
    }

    /// <summary>Says whether the host's OS family can put an extension in the requested state.</summary>
    /// <param name="availability">What the agent reported the family can do with the extension.</param>
    /// <param name="enable">The state asked for: true to switch on.</param>
    /// <returns>
    /// <see langword="true"/> when it can. An availability the agent did not state offers no action:
    /// the agent re-validates regardless, but the panel does not start a task for a request it cannot
    /// show is possible.
    /// </returns>
    private static bool CanBeSwitched(PhpExtensionAvailability availability, bool enable)
    {
        return availability switch
        {
            PhpExtensionAvailability.Available => true,
            PhpExtensionAvailability.DisableNotSupported => enable,
            _ => false,
        };
    }

    /// <summary>Journals the refusal as a failure and answers it.</summary>
    /// <param name="command">The request that was refused.</param>
    /// <param name="error">The typed failure to answer with, passed through exactly as decided.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<IReadOnlyList<PhpExtensionDto>>> RefuseAsync(
        SetPhpExtensionCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await RecordAsync(command, succeeded: false, cancellationToken);

        return Result<IReadOnlyList<PhpExtensionDto>>.Fail(error);
    }

    /// <summary>Writes the journal entry for this request.</summary>
    /// <param name="command">The request.</param>
    /// <param name="succeeded">Whether the extension is in the requested state.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    private async Task RecordAsync(SetPhpExtensionCommand command, bool succeeded, CancellationToken cancellationToken)
    {
        await _journal.RecordPhpExtensionChangedAsync(
            command.Version,
            command.Extension,
            command.Enabled,
            succeeded,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }

    /// <summary>Restates the agent's extensions as the administrator's outward view.</summary>
    /// <param name="extensions">The agent client's extensions.</param>
    /// <returns>
    /// The catalogue extensions, labelled, availability included; a name outside the panel's copy of the
    /// catalogue is dropped, as the list endpoint drops it.
    /// </returns>
    private List<PhpExtensionDto> Describe(
        IReadOnlyList<AgentPhpExtensionDto> extensions)
    {
        return extensions
            .Where(extension =>
            {
                return PhpExtensionCatalogue.IsKnown(extension.Name);
            })
            .Select(extension =>
            {
                return PhpExtensionMapper.ForAdministrator(
                    extension,
                    _displayNames.OfPhpExtension(extension.Name),
                    availability => { return _displayNames.Of(availability); });
            })
            .ToList();
    }

    /// <summary>Reads the extensions again and checks the one that was switched is as asked.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The failure, or <see langword="null"/> with the extensions as they are now. A list that cannot
    /// be read is the agent's own failure; a state that is not the one asked for is
    /// <see cref="ErrorMessages.PhpExtensionToggleFailed"/>.
    /// </returns>
    private async Task<(Error? Failure, IReadOnlyList<AgentPhpExtensionDto>? After)>
        ObserveAsync(SetPhpExtensionCommand command, CancellationToken cancellationToken)
    {
        var after = await _php.ListExtensionsAsync(command.Version, cancellationToken);
        if (!after.IsSuccess)
        {
            return (after.Error!, null);
        }

        var asked = after.Value.Any(extension =>
        {
            return string.Equals(extension.Name, command.Extension, StringComparison.Ordinal)
                && extension.Enabled == command.Enabled;
        });

        return asked ? (null, after.Value) : (ToggleFailed, null);
    }

    /// <summary>Consumes the change stream, reporting progress, and names the failure if there is one.</summary>
    /// <param name="command">The request.</param>
    /// <param name="taskId">The task to report progress against.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>
    /// <see langword="null"/> when the stream stated the extension is as asked; otherwise the failure,
    /// passed through exactly as the agent client typed it — code and kind. The one failure this
    /// handler names itself is an outcome it never observed.
    /// </returns>
    private async Task<Error?> RunAsync(SetPhpExtensionCommand command, Guid taskId, CancellationToken cancellationToken)
    {
        var terminal = false;
        Error? failure = null;

        await foreach (var change in _php.SetExtensionAsync(
            command.Version,
            command.Extension,
            command.Enabled,
            cancellationToken))
        {
            switch (change.Kind)
            {
                case PhpExtensionEventKind.Progress:
                    await _tasks.ReportAsync(taskId, (int)change.Percent, change.Stage, cancellationToken);
                    break;

                case PhpExtensionEventKind.Changed:
                    terminal = true;
                    await _tasks.ReportAsync(taskId, 100, change.Stage, cancellationToken);
                    break;

                // Everything that is not progress and not success ends the attempt. They are listed
                // rather than folded into a default so that a new event kind on the wire arrives
                // here as a compiler-visible decision instead of being silently read as a failure.
                case PhpExtensionEventKind.Failed:
                    terminal = true;
                    failure = change.Error ?? ToggleFailed;
                    break;

                // The stream ended without the agent stating whether the extension changed. These
                // carry no failure of their own, because none was reported.
                case PhpExtensionEventKind.Dropped:
                case PhpExtensionEventKind.Idle:
                case PhpExtensionEventKind.Truncated:
                case PhpExtensionEventKind.Cancelled:
                    terminal = true;
                    failure = ToggleFailed;
                    break;

                default:
                    terminal = true;
                    failure = ToggleFailed;
                    break;
            }

            if (terminal)
            {
                break;
            }
        }

        // A stream that ended with no terminal event proves nothing about the extension. It is NOT
        // reported as a success: "nothing threw" is not an observation of an outcome.
        return terminal ? failure : ToggleFailed;
    }
}
