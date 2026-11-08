using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.MoveFileEntries;

/// <summary>
/// Handles <see cref="MoveFileEntriesCommand"/>: admits the account and moves each entry through the
/// agent, one item at a time.
/// </summary>
/// <remarks>
/// Each entry keeps its own name in the destination folder. The batch runner caps the batch before the
/// first agent call, audits every item on its own and reports every outcome; a failure of one item does
/// not stop the others.
/// A move is one rename that never replaces anything and never falls back to copy and delete, so it can
/// never end half done. A destination folder inside the entry being moved is refused by the agent, which
/// is what stops a loop; the panel does not guess at it.
/// </remarks>
public sealed class MoveFileEntriesCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Runs the items in order, audits each and reports each outcome.</summary>
    private readonly FilesBatchRunner _runner;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<MoveFileEntriesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="runner">Runs the batch.</param>
    /// <param name="agent">The agent client that moves.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public MoveFileEntriesCommandHandler(
        FilesAccountGate gate,
        FilesBatchRunner runner,
        IAgentFilesClient agent,
        ILogger<MoveFileEntriesCommandHandler> logger)
    {
        _gate = gate;
        _runner = runner;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Moves each entry and reports each outcome.</summary>
    /// <param name="command">The validated command; see <see cref="MoveFileEntriesCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the batch; the items already done stay recorded.</param>
    /// <returns>
    /// One outcome per entry — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesBatchEmpty</c>
    /// or <c>FilesBatchTooLarge</c> when the batch cannot start.
    /// </returns>
    public async Task<Result<FileBatchResultDto>> HandleAsync(
        MoveFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileMoved, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileBatchResultDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;

        return await _runner.RunAsync(
            account,
            FileAuditActions.FileMoved,
            command.SourcePaths,
            origin,
            async (source, token) =>
            {
                var destination = FileEntryMapper.PathOf(
                    command.DestinationDirectory, FilesPathSegmentsPolicy.NameOf(source));

                var done = await _agent.MoveEntryAsync(account.Username, source, destination, token);

                return done.IsSuccess
                    ? Result.Ok()
                    : Result.Fail(
                        FilesAgentErrorTranslator.Translate(
                            _logger, done.Error!, nameof(_agent.MoveEntryAsync), command.AccountId.ToString()));
            },
            cancellationToken);
    }
}
