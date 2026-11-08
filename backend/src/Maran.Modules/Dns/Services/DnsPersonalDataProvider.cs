using System.Globalization;
using Maran.Modules.Dns.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the DNS zones an account owns and the
/// records in them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> A zone is exported by its name and creation time, a record by its
/// owner name, type, time to live and value. A record value can be personal data (a TXT record may carry an
/// e-mail address or a name), which is why the module answers at all. The settings row is the server's
/// configuration and not a record about an account's people.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class DnsPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DnsPersonalDataProvider(DnsDbContext dbContext)
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

        var zones = await _dbContext.Zones
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.CreatedAt })
            .ToListAsync(ct);

        var records = await _dbContext.ZoneRecords
            .AsNoTracking()
            .Where(row => row.Zone.AccountId == subject.Id)
            .OrderBy(row => row.Zone.Name)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, ZoneName = row.Zone.Name, row.Name, row.Type, row.Ttl, row.Value })
            .ToListAsync(ct);

        var zoneRecords = zones
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["name"] = row.Name,
                    ["createdAt"] = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();
        var recordRecords = records
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["zone"] = row.ZoneName,
                    ["name"] = row.Name,
                    ["type"] = row.Type.ToString(),
                    ["ttl"] = row.Ttl.ToString(CultureInfo.InvariantCulture),
                    ["value"] = row.Value,
                });
            })
            .ToList();

        return
        [
            new PersonalDataSection("dns", "zones", zoneRecords),
            new PersonalDataSection("dns", "records", recordRecords),
        ];
    }
}
