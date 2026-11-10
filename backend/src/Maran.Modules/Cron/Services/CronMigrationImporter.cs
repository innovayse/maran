using Maran.Modules.Cron.Commands.CreateCronEntry;
using Maran.Modules.Cron.Commands.SetCronEntryEnabled;
using Maran.Modules.Cron.Commands.SetCronEnvironment;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// Creates an imported account's cron environment and entries (issue #127). The Cron module's
/// <see cref="IAccountMigrationImporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every entry is created disabled</b> (decision D8b). The target is not live yet, and an entry that ran the
/// moment it was written would act on a copy of the customer's data while the original is still serving. The
/// dry run says so (<c>MigrationCronImportedDisabled</c>), and the administrator enables the entries that were
/// enabled on the sending server at cut-over, through the ordinary cron screen.
/// </para>
/// <para>
/// This importer runs after the databases and sites exist, because a cron command commonly refers to them.
/// </para>
/// </remarks>
public sealed class CronMigrationImporter : IAccountMigrationImporter
{
    /// <summary>The bus each entry is created through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the importer.</summary>
    /// <param name="bus">The bus each entry is created through.</param>
    public CronMigrationImporter(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return CronManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 60; }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<CronMigrationSection>(ModuleId, CronMigrationExporter.SectionVersion);

        if (payload is null)
        {
            IReadOnlyList<MigrationFinding> invalid =
                [new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.SectionInvalid, context.AccountName)];

            return Task.FromResult(invalid);
        }

        IReadOnlyList<MigrationFinding> findings = payload.Entries.Any(entry =>
        {
            return entry.Enabled;
        })
            ? [new MigrationFinding(ModuleId, MigrationFindingSeverity.Info, MigrationCodes.CronImportedDisabled, context.AccountName)]
            : [];

        return Task.FromResult(findings);
    }

    /// <inheritdoc />
    public async Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<CronMigrationSection>(ModuleId, CronMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation));
        }

        if (target.AccountId is not { } accountId)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.TargetAccountMissing, ErrorType.Failure));
        }

        if (payload.Environment.Count > 0)
        {
            var environment = await _bus.InvokeAsync<Result>(
                new SetCronEnvironmentCommand(accountId, payload.Environment),
                cancellationToken);

            if (!environment.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(environment.Error!);
            }
        }

        foreach (var entry in payload.Entries)
        {
            var created = await _bus.InvokeAsync<Result<CronEntryDto>>(
                new CreateCronEntryCommand(accountId, entry.Schedule, entry.Command),
                cancellationToken);

            if (!created.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(created.Error!);
            }

            // A new entry is created enabled by the ordinary command, because the agent installs it as a live
            // crontab line; this one is switched off at once so nothing runs on the target before cut-over.
            var disabled = await _bus.InvokeAsync<Result>(
                new SetCronEntryEnabledCommand(accountId, false, created.Value!.EntryId),
                cancellationToken);

            if (!disabled.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(disabled.Error!);
            }
        }

        return Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, []));
    }
}
