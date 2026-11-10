using Maran.Modules.Sftp.Models;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// Hands an account's SFTP logins to another server: names and public keys (issue #127). The Sftp module's
/// <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// Reads through the module's tenant-scoped context. Only <c>Label</c> and <c>PublicKey</c> of a key are read,
/// by name; nothing about a login's password is.
/// </remarks>
public sealed class SftpMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="SftpMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SftpMigrationExporter(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return SftpManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var users = await _dbContext.SftpUsers
            .AsNoTracking()
            .Include(user => user.Keys)
            .Where(user => user.AccountId == accountId)
            .OrderBy(user => user.Name)
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return null;
        }

        return new SftpMigrationSection(
            users.Select(user =>
            {
                return new SftpUserMigrationItem(
                    user.Name,
                    user.Keys
                        .OrderBy(key =>
                        {
                            return key.Label;
                        })
                        .Select(key =>
                        {
                            return new SftpKeyMigrationItem(key.Label, key.PublicKey);
                        })
                        .ToList());
            }).ToList()).ToSection(ModuleId, SectionVersion, users.Count);
    }
}
