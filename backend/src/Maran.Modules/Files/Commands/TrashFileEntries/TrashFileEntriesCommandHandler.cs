using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.TrashFileEntries;

/// <summary>Handles <see cref="TrashFileEntriesCommand"/>: admits the account and moves each entry into the account's trash through the agent.</summary>
/// <remarks>
/// A trashed entry is renamed within the same filesystem, so it is instant whatever its size and keeps
/// counting toward the account's quota until the retention removes it. The agent answers the id the entry
/// has in the trash; nothing is lost by not keeping it, because the listing reports it. The batch runner
/// caps the batch before the first agent call, audits every item on its own and reports every outcome.
/// </remarks>
public sealed class TrashFileEntriesCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Runs the items in order, audits each and reports each outcome.</summary>
    private readonly FilesBatchRunner _runner;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<TrashFileEntriesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="runner">Runs the batch.</param>
    /// <param name="agent">The agent client that moves entries to the trash.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public TrashFileEntriesCommandHandler(
        FilesAccountGate gate,
        FilesBatchRunner runner,
        IAgentFilesClient agent,
        ILogger<TrashFileEntriesCommandHandler> logger)
    {
        _gate = gate;
        _runner = runner;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Runs the batch and reports each outcome.</summary>
    /// <param name="command">The validated command; see <see cref="TrashFileEntriesCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the batch; the items already done stay recorded.</param>
    /// <returns>
    /// One outcome per item — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesBatchEmpty</c>
    /// or <c>FilesBatchTooLarge</c> when the batch cannot start.
    /// </returns>
    public async Task<Result<FileBatchResultDto>> HandleAsync(
        TrashFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileTrashed, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileBatchResultDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;

        return await _runner.RunAsync(
            account,
            FileAuditActions.FileTrashed,
            command.Paths,
            origin,
            async (subject, token) =>
            {
                var done = await _agent.TrashEntryAsync(account.Username, subject, token);

                return done.IsSuccess
                    ? Result.Ok()
                    : Result.Fail(
                        FilesAgentErrorTranslator.Translate(
                            _logger, done.Error!, nameof(_agent.TrashEntryAsync), command.AccountId.ToString()));
            },
            cancellationToken);
    }
}
