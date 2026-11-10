using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Models;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Hands an account's sites to another server: hostnames, backend and whether each was serving (issue #127).
/// The Sites module's <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// Reads through the module's tenant-scoped context, so a caller who may not see the account reads no sites.
/// </remarks>
public sealed class SitesMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="SitesMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SitesMigrationExporter(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return SitesManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var sites = await _dbContext.Sites
            .AsNoTracking()
            .Where(site => site.AccountId == accountId)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);

        if (sites.Count == 0)
        {
            return null;
        }

        return new SitesMigrationSection(
            sites.Select(site =>
            {
                return new SiteMigrationItem(
                    site.Domain,
                    site.Aliases,
                    site.BackendType,
                    site.PhpVersion,
                    site.ProxyUpstream,
                    site.Status == SiteStatus.Enabled);
            }).ToList()).ToSection(ModuleId, SectionVersion, sites.Count);
    }
}
