using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Hands an account's certificates to another server as metadata: the name, the source and the end date
/// (issue #127). The Ssl module's <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// Reads through the module's tenant-scoped context. The section is built from named columns of
/// <c>Certificate</c>, so a column added to it later is not exported by accident; the entity holds no key or
/// chain in any case, which stay on the server's disk and never leave it.
/// </remarks>
public sealed class SslMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="SslMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SslMigrationExporter(SslDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return SslManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Certificates
            .AsNoTracking()
            .Where(certificate => certificate.AccountId == accountId)
            .OrderBy(certificate => certificate.Domain)
            .Select(certificate => new { certificate.Domain, certificate.Source, certificate.IsWildcard, certificate.NotAfter })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var certificates = rows
            .Select(row =>
            {
                return new CertificateMigrationItem(row.Domain, row.Source.ToString(), row.IsWildcard, row.NotAfter);
            })
            .ToList();

        return new SslMigrationSection(certificates).ToSection(ModuleId, SectionVersion, certificates.Count);
    }
}
