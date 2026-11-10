using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Queries.ListInstalled;

/// <summary>
/// Handles <see cref="ListInstalledQuery"/>: what the loader runs, beside the ledger's history. Works with
/// the marketplace off and with it unreachable, because it reads only panel-side state.
/// </summary>
public sealed class ListInstalledQueryHandler
{
    /// <summary>The installed modules.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>The words, in the caller's language.</summary>
    private readonly MarketplaceDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="installed">The installed modules.</param>
    /// <param name="names">The words, in the caller's language.</param>
    public ListInstalledQueryHandler(InstalledModuleReader installed, MarketplaceDisplayNames names)
    {
        _installed = installed;
        _names = names;
    }

    /// <summary>Lists the installed modules.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per installed module, ordered by module id.</returns>
    public async Task<Result<IReadOnlyList<InstalledModuleDto>>> HandleAsync(
        ListInstalledQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var facts = await _installed.ReadAsync(cancellationToken);
        IReadOnlyList<InstalledModuleDto> modules = facts
            .Select(entry =>
            {
                var failure = entry.Loaded.State is ModuleLifecycleState.Degraded or ModuleLifecycleState.Failed
                    ? _names.SentenceOrNull(entry.Loaded.Failure)
                    : null;
                return InstalledModuleMapper.ToDto(entry, InstalledModuleReader.ActionsOf(entry), failure);
            })
            .ToList();

        return Result<IReadOnlyList<InstalledModuleDto>>.Ok(modules);
    }
}
