using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Queries.GetAccountImportReport;

/// <summary>
/// Handles <see cref="GetAccountImportReportQuery"/>: asks each module's importer to judge its section, and
/// states the union, conflicts first (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>It changes nothing.</b> Every importer's <c>PlanAsync</c> is read-only, and this handler writes no row.
/// The staged upload is disk, not account state: it expires, and discarding the import removes it.
/// </para>
/// <para>
/// The sentences are produced here, from the finding codes, in the caller's language; the screen only renders
/// them. A section no module on this server has an importer for is a warning: its contents will not be
/// imported, and the administrator is told rather than left to find out.
/// </para>
/// </remarks>
public sealed class GetAccountImportReportQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Every module's importer.</summary>
    private readonly IReadOnlyList<IAccountMigrationImporter> _importers;

    /// <summary>Resolves a finding code to its sentence.</summary>
    private readonly IErrorTextProvider _texts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="importers">Every module's importer.</param>
    /// <param name="texts">Resolves a finding code to its sentence.</param>
    public GetAccountImportReportQueryHandler(
        ServerMigrationDbContext dbContext,
        IEnumerable<IAccountMigrationImporter> importers,
        IErrorTextProvider texts)
    {
        _dbContext = dbContext;
        _importers = importers.OrderBy(importer =>
        {
            return importer.Order;
        }).ToList();
        _texts = texts;
    }

    /// <summary>Judges the import.</summary>
    /// <param name="query">The import and the plan.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report, or not found.</returns>
    public async Task<Result<AccountImportReportDto>> HandleAsync(
        GetAccountImportReportQuery query,
        CancellationToken cancellationToken)
    {
        var import = await _dbContext.AccountImports.AsNoTracking().FirstOrDefaultAsync(row => row.Id == query.ImportId, cancellationToken);

        if (import is null || import.State != AccountImportState.Staged)
        {
            return Result<AccountImportReportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound));
        }

        var sections = JsonSerializer.Deserialize<List<MigrationSection>>(import.SectionsJson, JsonSerializerOptions.Web) ?? [];
        var context = new MigrationPlanContext(
            import.SourceAccountName,
            query.PlanId,
            sections.ToDictionary(section =>
            {
                return section.ModuleId;
            }, section =>
            {
                return section.ResourceCount;
            }),
            import.HomeBytes);
        var findings = new List<MigrationFinding>();

        foreach (var importer in _importers)
        {
            var section = sections.FirstOrDefault(row =>
            {
                return row.ModuleId == importer.ModuleId;
            });

            if (section is null)
            {
                continue;
            }

            findings.AddRange(await importer.PlanAsync(section, context, cancellationToken));
        }

        foreach (var orphan in sections.Where(section =>
        {
            return _importers.All(importer =>
        {
            return importer.ModuleId != section.ModuleId;
        });
        }))
        {
            findings.Add(new MigrationFinding(
                orphan.ModuleId,
                MigrationFindingSeverity.Warning,
                nameof(ErrorMessages.MigrationSectionNotImportable),
                orphan.ModuleId));
        }

        if (sections.All(section =>
        {
            return section.ModuleId != "accounts";
        }))
        {
            findings.Add(new MigrationFinding(
                "accounts",
                MigrationFindingSeverity.Conflict,
                MigrationCodes.SectionInvalid,
                import.SourceAccountName));
        }

        var ordered = findings
            .OrderByDescending(finding =>
            {
                return finding.Severity;
            })
            .ThenBy(finding =>
            {
                return finding.ModuleId;
            }, StringComparer.Ordinal)
            .Select(finding =>
            {
                return new MigrationFindingDto(
                    finding.ModuleId,
                    finding.Severity.ToString(),
                    finding.Code,
                    _texts.Resolve(finding.Code),
                    finding.Subject);
            })
            .ToList();

        return Result<AccountImportReportDto>.Ok(
            new AccountImportReportDto(
                import.Id,
                ordered.All(finding =>
                {
                    return finding.Severity != nameof(MigrationFindingSeverity.Conflict);
                }),
                ordered));
    }
}
