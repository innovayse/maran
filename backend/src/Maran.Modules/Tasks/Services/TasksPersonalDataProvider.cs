using System.Globalization;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the background tasks the panel ran for an
/// account.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field, and the log is never on it.</b> A task's log is progress text
/// written by whatever ran, and its subject names the thing acted on in free text. Neither is read: the
/// query projects the identity, the kind, the status and the two times only.
/// </para>
/// <para>
/// The provider answers for an account subject only; a user subject gets an empty list.
/// </para>
/// </remarks>
public sealed class TasksPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public TasksPersonalDataProvider(TasksDbContext dbContext)
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

        var rows = await _dbContext.PanelTasks
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Kind, row.Status, row.StartedAt, row.FinishedAt })
            .ToListAsync(ct);

        var records = rows
            .Select(row =>
            {
                return new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["id"] = row.Id.ToString(),
                    ["kind"] = row.Kind,
                    ["status"] = row.Status.ToString(),
                    ["startedAt"] = row.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["finishedAt"] = row.FinishedAt?.ToString("O", CultureInfo.InvariantCulture),
                });
            })
            .ToList();

        return [new PersonalDataSection("tasks", "tasks", records)];
    }
}
