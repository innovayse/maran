using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Pagination;
using Microsoft.Extensions.Logging;
using AgentDirectorySortOrder = Maran.Agent.Client.Services.FilesService.DirectorySortOrder;

namespace Maran.Modules.Files.Queries.ListFileEntries;

/// <summary>Handles <see cref="ListFileEntriesQuery"/>: one page of a folder, served by the agent or, for a search, paged here.</summary>
/// <remarks>
/// <para>
/// The account is admitted through <see cref="FilesAccountGate"/> first, so another tenant's account
/// answers 404 and a suspended account answers the module's refusal before the agent is asked
/// anything. The agent is then addressed by the system user name the directory returned, never by a
/// name taken from the request.
/// </para>
/// <para>
/// This module keeps no rows: the folder is read live and nothing about it is stored or audited.
/// The agent journals the read itself.
/// </para>
/// </remarks>
public sealed class ListFileEntriesQueryHandler
{
    /// <summary>
    /// The most matches a search asks the agent for: the agent's own ceiling, so a search of one folder
    /// is never cut shorter by this module than by the agent.
    /// </summary>
    private const uint SearchResultCeiling = 500;

    /// <summary>Searching one folder looks at its direct entries only: depth one is the start directory.</summary>
    private const uint FolderSearchDepth = 1;

    /// <summary>The text members a search page re-checks, so the page and the agent agree on what a match is.</summary>
    private static readonly IReadOnlyList<Func<FileEntryDto, string>> SearchFields =
        [entry => { return entry.Name; }];

    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Names an entry's kind in the request's culture.</summary>
    private readonly FilesDisplayNames _names;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<ListFileEntriesQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="names">Names an entry's kind in the request's culture.</param>
    /// <param name="agent">The agent client that lists and searches.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public ListFileEntriesQueryHandler(
        FilesAccountGate gate,
        FilesDisplayNames names,
        IAgentFilesClient agent,
        ILogger<ListFileEntriesQueryHandler> logger)
    {
        _gate = gate;
        _names = names;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Reads one page.</summary>
    /// <param name="query">The validated query; see <see cref="ListFileEntriesQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The page — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, or the module's translation of the
    /// agent's refusal (a missing folder, a path that is not a folder, a folder too large to list).
    /// </returns>
    public async Task<Result<PagedResult<FileEntryDto>>> HandleAsync(
        ListFileEntriesQuery query,
        CancellationToken cancellationToken)
    {
        var admitted = await _gate.AdmitAsync(
            query.AccountId,
            FileAuditActions.FileAccessRefused,
            new FilesCallOrigin(query.IpAddress, query.UserAgent),
            cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<PagedResult<FileEntryDto>>.Fail(admitted.Error!);
        }

        var username = admitted.Value.Username;

        return query.Search.IsNone
            ? await ListFolderAsync(query, username, cancellationToken)
            : await SearchFolderAsync(query, username, cancellationToken);
    }

    /// <summary>Asks the agent for the requested page of the folder, ordered and paged by the agent.</summary>
    /// <param name="query">The validated query, with no search term.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or the translated refusal.</returns>
    private async Task<Result<PagedResult<FileEntryDto>>> ListFolderAsync(
        ListFileEntriesQuery query,
        string username,
        CancellationToken cancellationToken)
    {
        var order = ListFileEntriesSorts.Map.Resolve(query.Sort);

        // A page number past what the agent can address is asked for at the last offset it accepts, which
        // is past the end of any folder it will list, so the answer is an empty page with the true total.
        var offset = (uint)Math.Min(PageWindow.OffsetOf(query), uint.MaxValue);

        var listed = await _agent.ListDirectoryAsync(
            username,
            query.Path,
            offset,
            (uint)query.PageSize,
            query.ShowHidden,
            AgentSortOf(order.Field),
            order.Direction == SortDirection.Descending,
            cancellationToken);
        if (!listed.IsSuccess)
        {
            return Result<PagedResult<FileEntryDto>>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, listed.Error!, nameof(_agent.ListDirectoryAsync), query.AccountId.ToString()));
        }

        var listing = listed.Value;
        var items = listing.Entries
            .Select(entry =>
            {
                return FileEntryMapper.From(entry, query.Path, _names.KindOf(FileEntryMapper.KindOf(entry.Kind)));
            })
            .ToList();

        return Result<PagedResult<FileEntryDto>>.Ok(
            new PagedResult<FileEntryDto>(items, (int)listing.TotalEntries, query.Page, query.PageSize));
    }

    /// <summary>Asks the agent for the folder's entries whose name contains the term, then pages them here.</summary>
    /// <param name="query">The validated query, with a search term.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or the translated refusal.</returns>
    private async Task<Result<PagedResult<FileEntryDto>>> SearchFolderAsync(
        ListFileEntriesQuery query,
        string username,
        CancellationToken cancellationToken)
    {
        var found = await _agent.SearchEntriesAsync(
            username,
            query.Path,
            query.Search.Value,
            FolderSearchDepth,
            SearchResultCeiling,
            query.ShowHidden,
            cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<PagedResult<FileEntryDto>>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, found.Error!, nameof(_agent.SearchEntriesAsync), query.AccountId.ToString()));
        }

        var matches = found.Value.Matches
            .Select(match =>
            {
                return FileSearchMatchMapper.ToDto(match, _names.KindOf(FileEntryMapper.KindOf(match.Entry.Kind)));
            })
            .ToList();

        return Result<PagedResult<FileEntryDto>>.Ok(
            matches.ToPaged(query, ListFileEntriesSorts.Map, SearchFields));
    }

    /// <summary>Names the agent's order for a public sort field.</summary>
    /// <param name="field">A field of <see cref="ListFileEntriesSorts.Map"/>.</param>
    /// <returns>The agent's order.</returns>
    private static AgentDirectorySortOrder AgentSortOf(string field)
    {
        return field switch
        {
            FileEntrySortFields.Name => AgentDirectorySortOrder.Name,
            FileEntrySortFields.Modified => AgentDirectorySortOrder.Modified,
            FileEntrySortFields.Size => AgentDirectorySortOrder.Size,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a field of the sort map."),
        };
    }
}
