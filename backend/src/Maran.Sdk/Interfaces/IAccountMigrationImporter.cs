using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Judges and imports one module's section of a package on the target server (issue #127). Implemented by the
/// module that wrote the section, so an import is never a second, weaker way to create things: each section is
/// created through the module's own commands and checked by the module's own validators.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two operations with different promises.</b> <see cref="PlanAsync"/> is the dry run and changes nothing at
/// all; <see cref="ImportAsync"/> acts, as the administrator who started the import, through the module's own
/// handlers.
/// </para>
/// <para>
/// <b>Order.</b> Importers run in ascending <see cref="Order"/>. The Accounts importer is first and creates the
/// account; the databases follow, because a restore needs them to exist; sites, logins and cron entries come
/// after. Everything inside the home directory is supplied afterwards by the restore, so an importer creates
/// only panel rows and agent configuration that lives outside the home.
/// </para>
/// </remarks>
public interface IAccountMigrationImporter
{
    /// <summary>The id of the module this importer answers for.</summary>
    string ModuleId { get; }

    /// <summary>Where this importer runs among the others: ascending, the Accounts importer first.</summary>
    int Order { get; }

    /// <summary>Judges the section against this server without changing anything.</summary>
    /// <param name="section">The module's section of the package.</param>
    /// <param name="context">What the importer may need to know about the whole package.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every finding, conflicts included; empty when the section would import cleanly.</returns>
    Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken);

    /// <summary>Imports the section into the target account.</summary>
    /// <param name="section">The module's section of the package.</param>
    /// <param name="target">The account being imported into.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>What was done, or the typed failure of the first thing that could not be.</returns>
    Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken);
}
