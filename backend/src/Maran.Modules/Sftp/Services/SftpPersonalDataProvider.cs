using System.Globalization;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the SFTP logins an account owns and the
/// label, algorithm and fingerprint of each public key authorised for them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> The key's fingerprint identifies it without being it: the
/// public key text itself is left out, and so is every credential (the panel stores no SFTP password).
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class SftpPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SftpPersonalDataProvider(SftpDbContext dbContext)
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

        var users = await _dbContext.SftpUsers
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.FullName, row.CreatedAt })
            .ToListAsync(ct);

        var keys = await _dbContext.SftpUserKeys
            .AsNoTracking()
            .Where(row => row.SftpUser.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.SftpUserId, row.Label, row.Algorithm, row.Fingerprint, row.CreatedAt })
            .ToListAsync(ct);

        var userRecords = users
            .Select(user =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = user.Id.ToString(),
                    ["userName"] = user.FullName,
                    ["createdAt"] = user.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        var keyRecords = keys
            .Select(key =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = key.Id.ToString(),
                    ["userId"] = key.SftpUserId.ToString(),
                    ["label"] = key.Label,
                    ["algorithm"] = key.Algorithm,
                    ["fingerprint"] = key.Fingerprint,
                    ["createdAt"] = key.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return
        [
            new PersonalDataSection("sftp", "users", userRecords),
            new PersonalDataSection("sftp", "keys", keyRecords),
        ];
    }
}
