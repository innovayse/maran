using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rules <see cref="ModuleSchemaOwnershipTests"/> enforces on one module's persistence: it owns
/// the PostgreSQL schema named after it and nothing else, and says so in its manifest.
/// </summary>
/// <remarks>
/// <para>
/// A module's <c>Manifest.SchemaName</c> is declared data the administrator reads before installing
/// (rules/security.md item 13); it is worth exactly as much as the match between that declaration and
/// the MODEL the module's contexts build. So this law reads the model, not the source: a table can be
/// moved to another schema by a configuration class, an <c>OnModelCreating</c> line or a convention,
/// and only the model answers for all of them.
/// </para>
/// <para>
/// UNOBSERVED HERE: SQL the module sends that no model describes. <see cref="ModuleRawSqlScanner"/>
/// reads the marked raw-SQL sites for that, and cannot see SQL built from a variable.
/// </para>
/// </remarks>
public static class ModuleSchemaLaw
{
    /// <summary>A context's default schema is not the module's declared schema.</summary>
    public const string DefaultSchemaIsNotSchemaName = "DefaultSchemaIsNotSchemaName";

    /// <summary>An entity is mapped into a schema that is not the module's declared schema.</summary>
    public const string EntityMappedElsewhere = "EntityMappedElsewhere";

    /// <summary>The module has a context and declares no schema.</summary>
    public const string ContextWithoutSchemaName = "ContextWithoutSchemaName";

    /// <summary>The module declares a schema and has no context to own it.</summary>
    public const string SchemaNameWithoutContext = "SchemaNameWithoutContext";

    /// <summary>The module has a context and implements no <c>IModuleMigrations</c>.</summary>
    public const string ContextWithoutMigrations = "ContextWithoutMigrations";

    /// <summary>The module implements <c>IModuleMigrations</c> and has no context to migrate.</summary>
    public const string MigrationsWithoutContext = "MigrationsWithoutContext";

    /// <summary>Names every way one module's persistence breaks schema ownership.</summary>
    /// <param name="moduleId">The module's id, for the messages.</param>
    /// <param name="schemaName">The schema its manifest declares, or <c>null</c> for none.</param>
    /// <param name="contexts">The module's contexts, built far enough to read their models.</param>
    /// <param name="implementsMigrations">Whether the module's assembly implements <c>IModuleMigrations</c>.</param>
    /// <returns>One line per violation, each beginning with the rule's constant.</returns>
    public static IReadOnlyList<string> Violations(
        string moduleId,
        string? schemaName,
        IReadOnlyList<DbContext> contexts,
        bool implementsMigrations)
    {
        var found = new List<string>();

        if (contexts.Count > 0 && schemaName is null)
        {
            found.Add($"{ContextWithoutSchemaName}: '{moduleId}' builds a context and declares no SchemaName.");
        }

        if (contexts.Count == 0 && schemaName is not null)
        {
            found.Add($"{SchemaNameWithoutContext}: '{moduleId}' declares schema '{schemaName}' and owns no context.");
        }

        if (contexts.Count > 0 && !implementsMigrations)
        {
            found.Add($"{ContextWithoutMigrations}: '{moduleId}' owns a context and implements no IModuleMigrations.");
        }

        if (contexts.Count == 0 && implementsMigrations)
        {
            found.Add($"{MigrationsWithoutContext}: '{moduleId}' implements IModuleMigrations and owns no context.");
        }

        foreach (var context in contexts)
        {
            found.AddRange(ContextViolations(moduleId, schemaName, context));
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Judges one context's default schema and every table or view its model maps.</summary>
    /// <param name="moduleId">The module's id, for the messages.</param>
    /// <param name="schemaName">The schema its manifest declares, or <c>null</c> for none.</param>
    /// <param name="context">The context to judge.</param>
    /// <returns>The violations of this context.</returns>
    private static List<string> ContextViolations(string moduleId, string? schemaName, DbContext context)
    {
        var found = new List<string>();
        var contextName = context.GetType().Name;
        var defaultSchema = context.Model.GetDefaultSchema();

        if (!string.Equals(defaultSchema, schemaName, StringComparison.Ordinal))
        {
            found.Add(
                $"{DefaultSchemaIsNotSchemaName}: {contextName} of '{moduleId}' defaults to schema "
                + $"'{defaultSchema ?? "(none)"}', not '{schemaName ?? "(none)"}'.");
        }

        foreach (var entity in context.Model.GetEntityTypes())
        {
            var schema = entity.GetTableName() is not null ? entity.GetSchema() : entity.GetViewSchema();
            if (!string.Equals(schema ?? defaultSchema, schemaName, StringComparison.Ordinal))
            {
                found.Add(
                    $"{EntityMappedElsewhere}: {entity.ClrType.Name} in {contextName} of '{moduleId}' is mapped "
                    + $"into schema '{schema ?? defaultSchema ?? "(none)"}', not '{schemaName ?? "(none)"}'.");
            }
        }

        return found;
    }
}
