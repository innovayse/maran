using System.Globalization;
using Maran.Modules.Ftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the FTP logins an account owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> The login name and its creation time are exported. The panel
/// stores no FTP password, and the FTPS settings row is the server's configuration, not a record about an
/// account's people.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class FtpPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public FtpPersonalDataProvider(FtpDbContext dbContext)
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

        var rows = await _dbContext.FtpUsers
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.FullName, row.CreatedAt })
            .ToListAsync(ct);

        var records = rows
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["userName"] = row.FullName,
                    ["createdAt"] = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return [new PersonalDataSection("ftp", "users", records)];
    }
}
