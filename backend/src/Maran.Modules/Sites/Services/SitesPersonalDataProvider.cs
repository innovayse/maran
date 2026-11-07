using System.Globalization;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the sites an account owns and the host
/// names each one answers to.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> Both queries project named columns, so nothing else on the
/// entity is exported until it is named here. The document root, proxy upstream and PHP version are
/// configuration of the customer's hosting, not records about a person, and are left out.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class SitesPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SitesPersonalDataProvider(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(
        PersonalDataSubject subject,
        CancellationToken ct)
    {
        if (subject.Kind != PersonalDataSubjectKind.Account)
        {
            return [];
        }

        var sites = await _dbContext.Sites
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Domain, row.Status, row.CreatedAt })
            .ToListAsync(ct);

        var hostnames = await _dbContext.SiteHostnames
            .AsNoTracking()
            .Where(row => row.Site.AccountId == subject.Id)
            .OrderBy(row => row.Name)
            .Select(row => new { row.Name, row.SiteId })
            .ToListAsync(ct);

        var siteRecords = sites
            .Select(site =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = site.Id.ToString(),
                    ["domain"] = site.Domain,
                    ["status"] = site.Status.ToString(),
                    ["createdAt"] = site.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        var hostnameRecords = hostnames
            .Select(hostname =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["name"] = hostname.Name,
                    ["siteId"] = hostname.SiteId.ToString(),
                });
            })
            .ToList();

        return
        [
            new PersonalDataSection("sites", "sites", siteRecords),
            new PersonalDataSection("sites", "hostnames", hostnameRecords),
        ];
    }
}
