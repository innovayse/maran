using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Models;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// Hands an account's databases to another server: names and login names only (issue #127). The Databases
/// module's <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// Reads through the module's tenant-scoped context. The section is built from <c>Name</c> and
/// <c>DbUserNameSuffix</c> of a database and from <c>NameSuffix</c> and the grants of an additional user, by name,
/// so a secret column added to either entity later is not exported.
/// </remarks>
public sealed class DatabasesMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="DatabasesMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DatabasesMigrationExporter(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return DatabasesManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var databases = await _dbContext.Databases
            .AsNoTracking()
            .Where(database => database.AccountId == accountId)
            .OrderBy(database => database.Name)
            .Select(database => new DatabaseMigrationItem(database.Name, database.DbUserNameSuffix))
            .ToListAsync(cancellationToken);

        if (databases.Count == 0)
        {
            return null;
        }

        var names = await _dbContext.Databases
            .AsNoTracking()
            .Where(database => database.AccountId == accountId)
            .ToDictionaryAsync(
                database =>
                {
                    return database.Id;
                },
                database =>
                {
                    return database.Name;
                },
                cancellationToken);
        var users = await _dbContext.DatabaseUsers
            .AsNoTracking()
            .Include(user => user.Grants)
            .Where(user => user.AccountId == accountId)
            .OrderBy(user => user.NameSuffix)
            .ToListAsync(cancellationToken);

        // Named fields only: the suffix, and per grant the database's name and the privilege names. A grant on a
        // database the account no longer lists is not carried.
        var items = users
            .Select(user =>
            {
                var grants = user.Grants
                    .Where(grant =>
                    {
                        return names.ContainsKey(grant.DatabaseId);
                    })
                    .OrderBy(
                        grant =>
                        {
                            return names[grant.DatabaseId];
                        },
                        StringComparer.Ordinal)
                    .Select(grant =>
                    {
                        var privileges = DatabasePrivilegeSets.MembersOf(grant.Privileges)
                            .Select(privilege => { return privilege.ToString(); })
                            .ToList();

                        return new DatabaseUserGrantMigrationItem(names[grant.DatabaseId], privileges);
                    })
                    .ToList();

                return new DatabaseUserMigrationItem(user.NameSuffix, grants);
            })
            .ToList();

        return new DatabasesMigrationSection(databases, items).ToSection(ModuleId, SectionVersion, databases.Count);
    }
}
