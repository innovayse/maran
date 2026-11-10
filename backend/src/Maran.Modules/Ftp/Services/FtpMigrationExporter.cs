using Maran.Modules.Ftp.Models;
using Maran.Modules.Ftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Services;

/// <summary>
/// Hands an account's FTPS logins to another server: names only (issue #127). The Ftp module's
/// <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>Reads through the module's tenant-scoped context and never touches a password column.</remarks>
public sealed class FtpMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="FtpMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public FtpMigrationExporter(FtpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return FtpManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var names = await _dbContext.FtpUsers
            .AsNoTracking()
            .Where(user => user.AccountId == accountId)
            .OrderBy(user => user.Name)
            .Select(user => user.Name)
            .ToListAsync(cancellationToken);

        return names.Count == 0 ? null : new FtpMigrationSection(names).ToSection(ModuleId, SectionVersion, names.Count);
    }
}
