using System.Globalization;
using Maran.Modules.Cron.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the metadata of the runs the panel
/// recorded for an account's scheduled jobs.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field, and the output is never on it.</b> A run's standard output and
/// standard error are whatever the customer's program printed: customer content, possibly holding a
/// secret the program echoed. The query projects the identity, the start, the duration and the end kind
/// and exit code, so the two text columns are never read.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class CronPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly CronDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public CronPersonalDataProvider(CronDbContext dbContext)
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

        var rows = await _dbContext.CronRuns
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.EntryId,
                row.StartedAt,
                row.DurationMilliseconds,
                row.EndKind,
                row.EndExitCode,
            })
            .ToListAsync(ct);

        var records = rows
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["entryId"] = row.EntryId,
                    ["startedAt"] = row.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["durationMilliseconds"] = row.DurationMilliseconds.ToString(CultureInfo.InvariantCulture),
                    ["endKind"] = row.EndKind.ToString(),
                    ["exitCode"] = row.EndExitCode?.ToString(CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return [new PersonalDataSection("cron", "runs", records)];
    }
}
