using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.CopyFileEntries;

/// <summary>
/// Handles <see cref="CopyFileEntriesCommand"/>: admits the account and copies each entry through the
/// agent, one item at a time.
/// </summary>
/// <remarks>
/// Each entry keeps its own name in the destination folder. The batch runner caps the batch before the
/// first agent call, audits every item on its own and reports every outcome; a failure of one item does
/// not stop the others.
/// A copy is staged by the agent and renamed into place, so one that fails or is stopped leaves nothing
/// at the destination. The tree is measured first and refused as a whole when it is over the agent's
/// bounds, and anything but plain files and folders in it refuses the whole copy.
/// </remarks>
public sealed class CopyFileEntriesCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Runs the items in order, audits each and reports each outcome.</summary>
    private readonly FilesBatchRunner _runner;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<CopyFileEntriesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="runner">Runs the batch.</param>
    /// <param name="agent">The agent client that copies.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public CopyFileEntriesCommandHandler(
        FilesAccountGate gate,
        FilesBatchRunner runner,
        IAgentFilesClient agent,
        ILogger<CopyFileEntriesCommandHandler> logger)
    {
        _gate = gate;
        _runner = runner;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Copies each entry and reports each outcome.</summary>
    /// <param name="command">The validated command; see <see cref="CopyFileEntriesCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the batch; the items already done stay recorded.</param>
    /// <returns>
    /// One outcome per entry — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesBatchEmpty</c>
    /// or <c>FilesBatchTooLarge</c> when the batch cannot start.
    /// </returns>
    public async Task<Result<FileBatchResultDto>> HandleAsync(
        CopyFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileCopied, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileBatchResultDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;

        return await _runner.RunAsync(
            account,
            FileAuditActions.FileCopied,
            command.SourcePaths,
            origin,
            async (source, token) =>
            {
                var destination = FileEntryMapper.PathOf(
                    command.DestinationDirectory, FilesPathSegmentsPolicy.NameOf(source));

                var done = await _agent.CopyEntryAsync(
                account.Username,
                source,
                destination,
                FilesTreeBoundsPolicy.MaximumEntries,
                FilesTreeBoundsPolicy.MaximumBytes,
                token);

                return done.IsSuccess
                    ? Result.Ok()
                    : Result.Fail(
                        FilesAgentErrorTranslator.Translate(
                            _logger, done.Error!, nameof(_agent.CopyEntryAsync), command.AccountId.ToString()));
            },
            cancellationToken);
    }
}
