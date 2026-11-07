using System.Globalization;
using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the backups taken for an account and the
/// schedules that take them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> A backup is exported as its identity, kind, status, size and
/// times. The artifact digest, the failure and verification codes, the destination and the name of an
/// account that was deleted are left out: none is a record about a person, and a digest is the kind of
/// value this export never carries. The backup archive itself is customer content on the host.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class BackupsPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public BackupsPersonalDataProvider(BackupsDbContext dbContext)
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

        var backups = await _dbContext.Backups
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Kind, row.Status, row.SizeBytes, row.StartedAt, row.FinishedAt })
            .ToListAsync(ct);

        var schedules = await _dbContext.BackupSchedules
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.RetainCount, row.EnabledAt, row.LastRunAt })
            .ToListAsync(ct);

        var backupRecords = backups
            .Select(backup =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = backup.Id.ToString(),
                    ["kind"] = backup.Kind.ToString(),
                    ["status"] = backup.Status.ToString(),
                    ["sizeBytes"] = backup.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["startedAt"] = backup.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["finishedAt"] = backup.FinishedAt?.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        var scheduleRecords = schedules
            .Select(schedule =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = schedule.Id.ToString(),
                    ["retainCount"] = schedule.RetainCount.ToString(CultureInfo.InvariantCulture),
                    ["enabledAt"] = schedule.EnabledAt?.ToString("O", CultureInfo.InvariantCulture),
                    ["lastRunAt"] = schedule.LastRunAt?.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return
        [
            new PersonalDataSection("backups", "backups", backupRecords),
            new PersonalDataSection("backups", "schedules", scheduleRecords),
        ];
    }
}
