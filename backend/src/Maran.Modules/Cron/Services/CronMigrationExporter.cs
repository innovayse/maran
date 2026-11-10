using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Models;
using Maran.Modules.Cron.Queries.GetCronEnvironment;
using Maran.Modules.Cron.Queries.ListCronEntries;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// Hands an account's cron entries and environment to another server (issue #127). The Cron module's
/// <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// The module owns no table: the agent owns the crontab. The exporter asks the module's own queries, which run
/// under the caller's tenant scope, so a caller who may not see the account reads nothing.
/// </remarks>
public sealed class CronMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="CronMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The bus the module's queries are dispatched on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the exporter.</summary>
    /// <param name="bus">The bus the module's queries are dispatched on.</param>
    public CronMigrationExporter(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return CronManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var entries = await _bus.InvokeAsync<Result<IReadOnlyList<CronEntryDto>>>(
            new ListCronEntriesQuery(accountId),
            cancellationToken);
        var environment = await _bus.InvokeAsync<Result<IReadOnlyList<CronEnvironmentVariableDto>>>(
            new GetCronEnvironmentQuery(accountId),
            cancellationToken);

        if (!entries.IsSuccess || !environment.IsSuccess)
        {
            return null;
        }

        if (entries.Value!.Count == 0 && environment.Value!.Count == 0)
        {
            return null;
        }

        return new CronMigrationSection(
            entries.Value.Select(entry =>
            {
                return new CronEntryMigrationItem(entry.Schedule, entry.Command, entry.Enabled);
            }).ToList(),
            environment.Value!).ToSection(ModuleId, SectionVersion, entries.Value.Count);
    }
}
