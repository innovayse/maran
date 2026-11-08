namespace Maran.Host.Cli.Services;

/// <summary>
/// Cross-checks the two ways the panel can learn what to migrate: the database contexts the container
/// registers, and the <see cref="Maran.Sdk.Interfaces.IModuleMigrations"/> the modules declare. They
/// must describe the same set of schemas, in both directions.
/// </summary>
/// <remarks>
/// A context no module migrates would leave its schema empty while the run reports success; a module
/// that declares migrations for a schema no context maps would report success having migrated
/// nothing. Both look identical to a clean run in a log, so each is a refusal (rules/testing.md, a
/// check must be able to observe what it reports on).
/// </remarks>
public static class ModuleMigrationCoverage
{
    /// <summary>Throws when the contexts and the modules' migrations do not cover the same schemas.</summary>
    /// <param name="contextSchemas">Each registered context's full type name, with its default schema (null when it declares none).</param>
    /// <param name="migrationSchemas">The schema of every <see cref="Maran.Sdk.Interfaces.IModuleMigrations"/> the modules declare.</param>
    /// <exception cref="InvalidOperationException">A context has no migrations, or a migration has no context.</exception>
    public static void Verify(
        IReadOnlyDictionary<string, string?> contextSchemas,
        IReadOnlyCollection<string> migrationSchemas)
    {
        var declared = migrationSchemas.ToHashSet(StringComparer.Ordinal);
        var mapped = contextSchemas.Values
            .Where(schema => { return schema is not null; })
            .Select(schema => { return schema!; })
            .ToHashSet(StringComparer.Ordinal);

        var gaps = new List<string>();

        foreach (var (context, schema) in contextSchemas)
        {
            if (schema is null)
            {
                gaps.Add($"{context} declares no default schema, so no module's migrations can be matched to it");
            }
            else if (!declared.Contains(schema))
            {
                gaps.Add($"{context} (schema '{schema}') has no IModuleMigrations");
            }
        }

        foreach (var schema in declared.Where(schema => { return !mapped.Contains(schema); }))
        {
            gaps.Add($"IModuleMigrations for schema '{schema}' matches no registered context");
        }

        var duplicated = migrationSchemas
            .GroupBy(schema => { return schema; }, StringComparer.Ordinal)
            .Where(group => { return group.Count() > 1; })
            .Select(group => { return group.Key; });
        foreach (var schema in duplicated)
        {
            gaps.Add($"schema '{schema}' is migrated by more than one IModuleMigrations");
        }

        if (gaps.Count > 0)
        {
            throw new InvalidOperationException(
                "--migrate refuses to run because the registered contexts and the modules' migrations "
                + "disagree: " + string.Join("; ", gaps.OrderBy(gap => { return gap; }, StringComparer.Ordinal))
                + ". Reporting success here would leave a schema empty or claim a migration that never ran.");
        }
    }
}
