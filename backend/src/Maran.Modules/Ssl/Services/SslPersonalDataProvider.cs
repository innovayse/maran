using System.Globalization;
using Maran.Modules.Ssl.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the certificates an account holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> A certificate is exported as its domain, where it came from
/// (the issuer: the public authority or the customer's own), when it was issued and when it expires.
/// The private key and the certificate material never leave the host or the encrypted columns, and the
/// ACME account of the server is not an account's row. The renewal bookkeeping is operational state, not a
/// record about a person.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class SslPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SslPersonalDataProvider(SslDbContext dbContext)
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

        var rows = await _dbContext.Certificates
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.IssuedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.SiteId, row.Domain, row.Source, row.IssuedAt, row.NotAfter })
            .ToListAsync(ct);

        var records = rows
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["siteId"] = row.SiteId.ToString(),
                    ["domain"] = row.Domain,
                    ["issuer"] = row.Source.ToString(),
                    ["issuedAt"] = row.IssuedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["notAfter"] = row.NotAfter.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return [new PersonalDataSection("ssl", "certificates", records)];
    }
}
