using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Commands.RestoreTrashEntries;

/// <summary>Handles <see cref="RestoreTrashEntriesCommand"/>: admits the account and moves each trash entry back to where it was through the agent.</summary>
/// <remarks>
/// An entry whose old place is taken again is not restored over it: the agent leaves it in the trash and
/// the item fails with <c>FilesRestoreTargetExists</c>, which is a more useful sentence here than the
/// generic "already exists" the same refusal is elsewhere. The subject of each audit entry is the trash id,
/// because the entry's path is the customer's text read from a file they can edit. The batch runner caps
/// the batch before the first agent call and reports every outcome.
/// </remarks>
public sealed class RestoreTrashEntriesCommandHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Runs the items in order, audits each and reports each outcome.</summary>
    private readonly FilesBatchRunner _runner;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<RestoreTrashEntriesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="runner">Runs the batch.</param>
    /// <param name="agent">The agent client that restores entries.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public RestoreTrashEntriesCommandHandler(
        FilesAccountGate gate,
        FilesBatchRunner runner,
        IAgentFilesClient agent,
        ILogger<RestoreTrashEntriesCommandHandler> logger)
    {
        _gate = gate;
        _runner = runner;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Runs the batch and reports each outcome.</summary>
    /// <param name="command">The validated command; see <see cref="RestoreTrashEntriesCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the batch; the items already done stay recorded.</param>
    /// <returns>
    /// One outcome per item — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesBatchEmpty</c>
    /// or <c>FilesBatchTooLarge</c> when the batch cannot start.
    /// </returns>
    public async Task<Result<FileBatchResultDto>> HandleAsync(
        RestoreTrashEntriesCommand command,
        CancellationToken cancellationToken)
    {
        var origin = new FilesCallOrigin(command.IpAddress, command.UserAgent);
        var admitted = await _gate.AdmitAsync(command.AccountId, FileAuditActions.FileRestored, origin, cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileBatchResultDto>.Fail(admitted.Error!);
        }

        var account = admitted.Value;

        return await _runner.RunAsync(
            account,
            FileAuditActions.FileRestored,
            command.TrashIds,
            origin,
            async (subject, token) =>
            {
                var done = await _agent.RestoreTrashEntryAsync(account.Username, subject, string.Empty, token);

                return done.IsSuccess
                    ? Result.Ok()
                    : Result.Fail(RestoreRefusal(
                        FilesAgentErrorTranslator.Translate(
                            _logger, done.Error!, nameof(_agent.RestoreTrashEntryAsync), command.AccountId.ToString())));
            },
            cancellationToken);
    }

    /// <summary>Names the refusal of a restore whose place is taken for what it is.</summary>
    /// <param name="translated">The module's translation of the agent's refusal.</param>
    /// <returns>
    /// <c>FilesRestoreTargetExists</c> when the translation is "already exists"; any other refusal unchanged.
    /// </returns>
    private static Error RestoreRefusal(Error translated)
    {
        return translated.Code == nameof(ErrorMessages.FilesEntryExists)
            ? Error.Of(nameof(ErrorMessages.FilesRestoreTargetExists), ErrorType.Conflict)
            : translated;
    }
}
