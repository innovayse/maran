using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Answers, for one account, the two questions every pool write asks: which PHP settings its pools
/// carry, and which PHP versions it has pools at (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>One source for every writer of a pool.</b> The agent renders a pool from exactly the settings it
/// is given and rewrites the file whenever that differs from disk, so a pool write that leaves the
/// settings out silently drops them: site creation, a version switch and the start-up convergence each
/// used to send an empty list. They now all ask here, so the answer cannot differ between them.
/// </para>
/// <para>
/// <b>It reads the account's rows whoever is signed in, on purpose.</b> The caller of a pool write is
/// not always the account's customer: an administrator or a reseller creates a site for a customer, the
/// start-up pass has no principal at all, and a plan change is announced inside the request of whoever
/// edited the plan. A tenant-filtered read would answer "no settings" in each of those cases, and the
/// pool would be rewritten without the customer's choices. Each read is bounded by the one account id
/// it is asked for and is not reachable from a request that names an account of its own.
/// </para>
/// </remarks>
public sealed class PhpAccountPoolSettings
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the source.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public PhpAccountPoolSettings(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>The settings an account's pools carry: what the customer chose, cut to the plan's bounds.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="limits">The plan's bounds for the account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings in the shape the agent client sends; empty when the customer chose none.</returns>
    public async Task<IReadOnlyList<PhpSettingDto>> EffectiveAsync(
        Guid accountId,
        PhpLimitsSnapshot limits,
        CancellationToken cancellationToken)
    {
        var stored = await StoredAsync(accountId, cancellationToken);

        return PhpSettingMapper.ToAgent(PhpSettingsPlanClamp.Apply(stored, limits));
    }

    /// <summary>The settings an account's customer chose, as stored.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account's rows, untracked.</returns>
    public async Task<IReadOnlyList<PhpSetting>> StoredAsync(Guid accountId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a pool write must carry the account's settings whoever asked for it; bounded by one account id
        return await _dbContext.PhpSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => setting.AccountId == accountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <summary>The PHP versions an account has a pool at, one pool per account and version.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The distinct versions of the account's PHP sites, in version order; empty when it has none.</returns>
    public async Task<IReadOnlyList<string>> VersionsInUseAsync(Guid accountId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the pools of an account are rewritten whoever edited its plan; bounded by one account id
        var versions = await _dbContext.Sites
            .IgnoreQueryFilters()
            .Where(site => site.AccountId == accountId && site.BackendType == SiteBackendType.Php)
            .Select(site => site.PhpVersion)
            .Distinct()
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        return versions
            .Where(version =>
            {
                return !string.IsNullOrEmpty(version);
            })
            .OrderBy(version => { return version; }, StringComparer.Ordinal)
            .ToList();
    }
}
