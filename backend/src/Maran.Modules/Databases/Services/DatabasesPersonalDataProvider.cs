using System.Globalization;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the names of the databases an account
/// owns and of the database users that open them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> The names and the creation time are exported. The panel stores
/// no database password (the engine holds it), and the contents of a database are customer content, not
/// a record the panel keeps about a person.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class DatabasesPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DatabasesPersonalDataProvider(DatabasesDbContext dbContext)
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

        var rows = await _dbContext.Databases
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.FullName, row.DbUserName, row.CreatedAt })
            .ToListAsync(ct);

        var records = rows
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["databaseName"] = row.FullName,
                    ["databaseUserName"] = row.DbUserName,
                    ["createdAt"] = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return [new PersonalDataSection("databases", "databases", records)];
    }
}
