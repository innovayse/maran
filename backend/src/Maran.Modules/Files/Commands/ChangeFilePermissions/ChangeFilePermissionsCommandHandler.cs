using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.ChangeFilePermissions;

/// <summary>
/// Handles <see cref="ChangeFilePermissionsCommand"/>: admits the account and sets the entry's permission
/// bits through the agent.
/// </summary>
/// <remarks>
/// <para>
/// A recursive change gives folders their own mode (see <see cref="ChangeFilePermissionsCommand.DirectoryMode"/>),
/// because files and folders need different bits: a folder without execute cannot be entered.
/// </para>
/// <para>
/// The answer has the shape of a one-item batch, so the SPA reads every mutation's outcome the same way,
/// and the batch runner journals the entry and reports the item. A recursive change is still one agent
/// call and one journal entry: the entry says which tree was changed, not how many entries it held.
/// </para>
/// <para>
/// The agent says how many entries it changed and how many it left alone (a link inside the tree, an entry
/// the account does not own). The panel logs both counts and does not turn "some were left alone" into a
/// failure: nothing was refused, and a link has no permission bits of its own to change.
/// </para>
/// </remarks>
public sealed class ChangeFilePermissionsCommandHandler
{
    /// <summary>Pre-compiled log delegate for a completed change.</summary>
    private static readonly Action<ILogger, ulong, ulong, Exception?> LogChanged =
        LoggerMessage.Define<ulong, ulong>(
            LogLevel.Information,
            new EventId(1, nameof(ChangeFilePermissionsCommandHandler)),
            "File permissions changed on {Changed} entries; {Skipped} were left alone");

    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Runs the one item, audits it and reports its outcome.</summary>
    private readonly FilesBatchRunner _runner;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<ChangeFilePermissionsCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="runner">Runs the one item.</param>
    /// <param name="agent">The agent client that changes permissions.</param>
    /// <param name="logger">Where an agent refusal and the counts are reported.</param>
    public ChangeFilePermissionsCommandHandler(
        FilesAccountGate gate,
        FilesBatchRunner runner,
        IAgentFilesClient agent,
        ILogger<ChangeFilePermissionsCommandHandler> logger)
    {
        _gate = gate;
        _runner = runner;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Changes the permission bits and reports the outcome.</summary>
    /// <param name="command">The validated command; see <see cref="ChangeFilePermissionsCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The outcome of the one item — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c> or
    /// <c>FilesModeInvalid</c>.
    /// </returns>
    public async Task<Result<FileBatchResultDto>> HandleAsync(
        ChangeFilePermissionsCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(
            command.AccountId, FileAuditActions.FilePermissionsChanged, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileBatchResultDto>.Fail(admitted.Error!);
        }

        // The validator already refused a mode that does not parse; this is the second look a command
        // dispatched on the bus without it deserves.
        if (!FilesModePolicy.TryParse(command.Mode, out var mode))
        {
            return Result<FileBatchResultDto>.Fail(Error.Of(nameof(ErrorMessages.FilesModeInvalid), ErrorType.Validation));
        }

        // Absent, folders get the file mode with execute added wherever read is set; present, the validator
        // already refused zero, which the agent would read as "the same as the mode".
        var directoryMode = FilesModePolicy.DirectoryModeFor(mode);
        if (command.DirectoryMode is not null
            && !FilesModePolicy.TryParseFolderMode(command.DirectoryMode, out directoryMode))
        {
            return Result<FileBatchResultDto>.Fail(Error.Of(nameof(ErrorMessages.FilesModeInvalid), ErrorType.Validation));
        }

        var account = admitted.Value;

        return await _runner.RunAsync(
            account,
            FileAuditActions.FilePermissionsChanged,
            [command.Path],
            origin,
            async (subject, token) =>
            {
                var done = await _agent.ChangePermissionsAsync(
                    account.Username, subject, mode, command.Recursive, directoryMode, token);
                if (!done.IsSuccess)
                {
                    return Result.Fail(
                        FilesAgentErrorTranslator.Translate(
                            _logger, done.Error!, nameof(_agent.ChangePermissionsAsync), command.AccountId.ToString()));
                }

                LogChanged(_logger, done.Value.Changed, done.Value.Skipped, null);
                return Result.Ok();
            },
            cancellationToken);
    }
}
