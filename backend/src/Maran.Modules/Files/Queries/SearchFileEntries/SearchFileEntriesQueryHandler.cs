using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Queries.ListFileEntries;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Queries.SearchFileEntries;

/// <summary>Handles <see cref="SearchFileEntriesQuery"/>: asks the agent for the matches below a folder and pages them here.</summary>
/// <remarks>
/// The depth, the result count and the number of entries examined are the agent's bounds, not this
/// module's: when one of them stopped the search the answer says so (<see cref="FileSearchPageDto.Truncated"/>)
/// and is still a success. The account is admitted first, exactly as the listing does.
/// </remarks>
public sealed class SearchFileEntriesQueryHandler
{
    /// <summary>
    /// The most matches a search asks for: the agent's own ceiling, so this module never cuts a search
    /// shorter than the agent would.
    /// </summary>
    private const uint ResultCeiling = 500;

    /// <summary>The deepest level a search asks for: the agent's own ceiling.</summary>
    private const uint DepthCeiling = 16;

    /// <summary>The text members a page re-checks, so the page and the agent agree on what a match is.</summary>
    private static readonly IReadOnlyList<Func<FileEntryDto, string>> SearchFields =
        [entry => { return entry.Name; }];

    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Names an entry's kind in the request's culture.</summary>
    private readonly FilesDisplayNames _names;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<SearchFileEntriesQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="names">Names an entry's kind in the request's culture.</param>
    /// <param name="agent">The agent client that searches.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public SearchFileEntriesQueryHandler(
        FilesAccountGate gate,
        FilesDisplayNames names,
        IAgentFilesClient agent,
        ILogger<SearchFileEntriesQueryHandler> logger)
    {
        _gate = gate;
        _names = names;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Searches and pages.</summary>
    /// <param name="query">The validated query; see <see cref="SearchFileEntriesQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>
    /// The page — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, or the module's translation of
    /// the agent's refusal.
    /// </returns>
    public async Task<Result<FileSearchPageDto>> HandleAsync(
        SearchFileEntriesQuery query,
        CancellationToken cancellationToken)
    {
        var admitted = await _gate.AdmitAsync(
            query.AccountId,
            FileAuditActions.FileAccessRefused,
            new FilesCallOrigin(query.IpAddress, query.UserAgent),
            cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileSearchPageDto>.Fail(admitted.Error!);
        }

        var found = await _agent.SearchEntriesAsync(
            admitted.Value.Username,
            query.Path,
            query.Search.Value,
            DepthCeiling,
            ResultCeiling,
            query.ShowHidden,
            cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<FileSearchPageDto>.Fail(
                FilesAgentErrorTranslator.Translate(
                    _logger, found.Error!, nameof(_agent.SearchEntriesAsync), query.AccountId.ToString()));
        }

        var page = found.Value.Matches
            .Select(match =>
            {
                return FileSearchMatchMapper.ToDto(match, _names.KindOf(FileEntryMapper.KindOf(match.Entry.Kind)));
            })
            .ToList()
            .ToPaged(query, ListFileEntriesSorts.Map, SearchFields);

        return Result<FileSearchPageDto>.Ok(
            new FileSearchPageDto(page.Items, page.Total, page.Page, page.PageSize, found.Value.Truncated));
    }
}
