using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Resources;
using Microsoft.Extensions.Logging;
using AgentDirectorySortOrder = Maran.Agent.Client.Services.FilesService.DirectorySortOrder;
using AgentFileEntry = Maran.Agent.Client.Services.FilesService.FileEntryDto;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Finds ONE entry of an account's home by its path: the answer to "what is at this path, and how big
/// is it, and when did it last change", for the operations that need it before they act on it.
/// </summary>
/// <remarks>
/// <para>
/// The agent has no rpc that describes a single entry. It lists a folder, a page at a time and in a
/// name order of its own, so this reads the entry's parent folder name-ordered, a thousand names a
/// page (the agent's page ceiling), and stops at the first page that holds the name. A folder of fewer
/// than a thousand entries costs one rpc; the agent lists at most a hundred thousand names, so the
/// worst case is a hundred and is bounded. Hidden entries are listed: a dot-file is as much an entry
/// as any other. When the agent gains an rpc that describes one entry this is the only file that
/// changes.
/// </para>
/// <para>
/// It is the single place that turns the agent's refusal of a lookup into this module's error, so a
/// caller returns the failure as it is and never translates it twice; an entry the folder does not
/// hold is <c>FilesEntryNotFound</c>, the same answer the agent's own not-found is translated to.
/// </para>
/// </remarks>
public sealed class FilesEntryLookup
{
    /// <summary>The names asked for per page: the agent's own ceiling for one listing page.</summary>
    private const uint PageSize = 1000;

    /// <summary>The agent, which owns the files — this module's only store.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>Where an agent refusal leaves its code and the operation, and nothing else.</summary>
    private readonly ILogger<FilesEntryLookup> _logger;

    /// <summary>Creates the lookup.</summary>
    /// <param name="agent">The agent client that lists the folder.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and operation only.</param>
    public FilesEntryLookup(IAgentFilesClient agent, ILogger<FilesEntryLookup> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Finds the entry at a path.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="accountId">The owning account, named in a refusal's log line.</param>
    /// <param name="path">The entry, relative to the home; a validated, non-empty path.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The agent's description of the entry — or <c>FilesEntryNotFound</c> when the folder holds no such
    /// name, or the module's translation of the agent's refusal (a missing parent, a parent that is not a folder).
    /// </returns>
    public async Task<Result<AgentFileEntry>> FindAsync(
        string accountUsername,
        Guid accountId,
        string path,
        CancellationToken cancellationToken)
    {
        var parent = FilesPathSegmentsPolicy.ParentOf(path);
        var name = FilesPathSegmentsPolicy.NameOf(path);

        uint offset = 0;
        while (true)
        {
            var listed = await _agent.ListDirectoryAsync(
                accountUsername,
                parent,
                offset,
                PageSize,
                includeHidden: true,
                AgentDirectorySortOrder.Name,
                descending: false,
                cancellationToken);
            if (!listed.IsSuccess)
            {
                return Result<AgentFileEntry>.Fail(
                    FilesAgentErrorTranslator.Translate(
                        _logger, listed.Error!, nameof(_agent.ListDirectoryAsync), accountId.ToString()));
            }

            var page = listed.Value;
            var found = page.Entries.FirstOrDefault(entry =>
            {
                return string.Equals(entry.Name, name, StringComparison.Ordinal);
            });
            if (found is not null)
            {
                return Result<AgentFileEntry>.Ok(found);
            }

            offset += (uint)page.Entries.Count;
            if (page.Entries.Count == 0 || offset >= page.TotalEntries)
            {
                return Result<AgentFileEntry>.Fail(
                    Error.Of(nameof(ErrorMessages.FilesEntryNotFound), ErrorType.NotFound));
            }
        }
    }
}
