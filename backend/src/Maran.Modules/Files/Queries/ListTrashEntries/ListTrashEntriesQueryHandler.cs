using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Queries.ListTrashEntries;

/// <summary>Handles <see cref="ListTrashEntriesQuery"/>: reads the account's trash through the agent and pages it here.</summary>
/// <remarks>
/// <para>
/// The account is admitted through <see cref="FilesAccountGate"/> first, so another tenant's account
/// answers 404 and a suspended account answers the module's refusal before the agent is asked anything.
/// </para>
/// <para>
/// The agent bounds the listing at its own ceiling and reports the newest entries, so a trash holding more
/// shows its newest; the oldest are still removed by the retention job. Nothing is audited: a read
/// writes no entry, and the agent journals it.
/// </para>
/// </remarks>
public sealed class ListTrashEntriesQueryHandler
{
    /// <summary>The text members a search looks in: the entry's name and the path it was trashed from.</summary>
    private static readonly IReadOnlyList<Func<TrashEntryDto, string>> SearchFields =
    [
        entry => { return entry.Name; },
        entry => { return entry.OriginalPath; },
    ];

    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>The agent, which owns the files and the trash beside them.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Names an entry's kind in the request's culture.</summary>
    private readonly FilesDisplayNames _names;

    /// <summary>The retention, which fixes when each entry expires.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<ListTrashEntriesQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="agent">The agent client that lists the trash.</param>
    /// <param name="names">Names an entry's kind.</param>
    /// <param name="options">The module's limits.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public ListTrashEntriesQueryHandler(
        FilesAccountGate gate,
        IAgentFilesClient agent,
        FilesDisplayNames names,
        IOptionsMonitor<FilesOptions> options,
        ILogger<ListTrashEntriesQueryHandler> logger)
    {
        _gate = gate;
        _agent = agent;
        _names = names;
        _options = options;
        _logger = logger;
    }

    /// <summary>Reads one page of the trash.</summary>
    /// <param name="query">The validated query; see <see cref="ListTrashEntriesQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The page — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, or the module's translation of the
    /// agent's refusal.
    /// </returns>
    public async Task<Result<PagedResult<TrashEntryDto>>> HandleAsync(
        ListTrashEntriesQuery query,
        CancellationToken cancellationToken)
    {
        var admitted = await _gate.AdmitAsync(
            query.AccountId,
            FileAuditActions.FileAccessRefused,
            new FilesCallOrigin(query.IpAddress, query.UserAgent),
            cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<PagedResult<TrashEntryDto>>.Fail(admitted.Error!);
        }

        var listed = await _agent.ListTrashAsync(admitted.Value.Username, cancellationToken);
        if (!listed.IsSuccess)
        {
            return Result<PagedResult<TrashEntryDto>>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, listed.Error!, nameof(_agent.ListTrashAsync), query.AccountId.ToString()));
        }

        var retentionDays = _options.CurrentValue.TrashRetentionDays;
        var entries = listed.Value
            .Select(entry =>
            {
                return TrashEntryMapper.From(
                    entry, retentionDays, _names.KindOf(entry.IsDirectory ? FileEntryKind.Directory : FileEntryKind.File));
            })
            .ToList();

        return Result<PagedResult<TrashEntryDto>>.Ok(entries.ToPaged(query, ListTrashEntriesSorts.Map, SearchFields));
    }
}
