using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Interfaces;

namespace Maran.ArchitectureTests;

/// <summary>
/// A module owns the PostgreSQL schema named after it and no other: its contexts default to that
/// schema, no entity is mapped elsewhere, no marked raw-SQL site names another module's schema, and
/// what its manifest declares matches what its model does (rules/architecture.md "Backend: modular
/// monolith").
/// </summary>
/// <remarks>
/// <para>
/// The manifest's <c>SchemaName</c> is the claim; the model is the fact. Each rule is shown on both
/// sides: the real modules come out clean, a planted context (<see cref="PlantedForeignSchemaContext"/>)
/// and a planted SQL string must be flagged by rule name, and the real tree is the accepting control.
/// </para>
/// <para>
/// The census is held against a second source — every context compiled into a module assembly — so a
/// context that fell out of <see cref="ModuleRegistry"/> shows up as a count mismatch and not as a
/// clean run over fewer modules.
/// </para>
/// </remarks>
public sealed class ModuleSchemaOwnershipTests
{
    /// <summary>The module folder prefix of the assembly names.</summary>
    private const string AssemblyPrefix = "Maran.Modules.";

    /// <summary>Every composed module's persistence matches what its manifest declares.</summary>
    [Fact]
    public void Every_modules_persistence_matches_the_schema_its_manifest_declares()
    {
        var violations = RealViolations();

        Assert.True(
            violations.Count == 0,
            "A module's model and its manifest disagree about the schema it owns (docs/modules/module-contract.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The census walked every context the build compiles into a composed module, and at least one; the
    /// modules built and not yet composed are the single <c>ModuleContractCensus.UnregisteredModules</c> list.
    /// </summary>
    [Fact]
    public void The_census_walked_every_context_compiled_into_a_module()
    {
        var built = ModuleDbContexts.CreateAll().Select(context => { return context.GetType().FullName; });
        var compiled = TenantScopeCensus.ContextTypesCompiledIntoRegisteredModules()
            .Select(type => { return type.FullName; });

        Assert.NotEmpty(built);
        Assert.Equal(compiled.Order(StringComparer.Ordinal), built.Order(StringComparer.Ordinal));
    }

    /// <summary>A context whose table is mapped into another module's schema is flagged by name.</summary>
    [Fact]
    public void A_planted_entity_mapped_into_another_modules_schema_is_flagged()
    {
        using var context = PlantedForeignSchemaContext.Create();

        var violations = ModuleSchemaLaw.Violations(
            PlantedForeignSchemaContext.OwnSchema,
            PlantedForeignSchemaContext.OwnSchema,
            [context],
            implementsMigrations: true);

        Assert.True(Flags(violations, ModuleSchemaLaw.EntityMappedElsewhere), Describe(violations));
        Assert.False(Flags(violations, ModuleSchemaLaw.DefaultSchemaIsNotSchemaName), Describe(violations));
    }

    /// <summary>A context whose default schema differs from the declared schema is flagged by name.</summary>
    [Fact]
    public void A_planted_context_with_another_default_schema_is_flagged()
    {
        using var context = PlantedForeignSchemaContext.Create();

        var violations = ModuleSchemaLaw.Violations(
            "someone",
            "someone",
            [context],
            implementsMigrations: true);

        Assert.True(Flags(violations, ModuleSchemaLaw.DefaultSchemaIsNotSchemaName), Describe(violations));
    }

    /// <summary>A module declaring no schema while owning a context is flagged, and the reverse.</summary>
    [Fact]
    public void A_schema_declaration_that_does_not_match_the_contexts_is_flagged()
    {
        using var context = PlantedForeignSchemaContext.Create();

        var contextWithoutSchema = ModuleSchemaLaw.Violations("planted", null, [context], implementsMigrations: true);
        var schemaWithoutContext = ModuleSchemaLaw.Violations("planted", "planted", [], implementsMigrations: false);

        Assert.True(Flags(contextWithoutSchema, ModuleSchemaLaw.ContextWithoutSchemaName), Describe(contextWithoutSchema));
        Assert.True(Flags(schemaWithoutContext, ModuleSchemaLaw.SchemaNameWithoutContext), Describe(schemaWithoutContext));
    }

    /// <summary>A module whose migrations seam does not match its contexts is flagged, in both directions.</summary>
    [Fact]
    public void A_migrations_seam_that_does_not_match_the_contexts_is_flagged()
    {
        using var context = PlantedForeignSchemaContext.Create();

        var missing = ModuleSchemaLaw.Violations("planted", "planted", [context], implementsMigrations: false);
        var surplus = ModuleSchemaLaw.Violations("planted", null, [], implementsMigrations: true);

        Assert.True(Flags(missing, ModuleSchemaLaw.ContextWithoutMigrations), Describe(missing));
        Assert.True(Flags(surplus, ModuleSchemaLaw.MigrationsWithoutContext), Describe(surplus));
    }

    /// <summary>A module with no context and no schema, and no migrations seam, is accepted.</summary>
    [Fact]
    public void A_module_with_no_persistence_and_no_schema_declaration_is_accepted()
    {
        var violations = ModuleSchemaLaw.Violations("planted", null, [], implementsMigrations: false);

        Assert.Empty(violations);
    }

    /// <summary>The scanner finds a planted foreign table in a marked raw-SQL site, in either spelling.</summary>
    [Fact]
    public void The_scanner_finds_a_planted_foreign_table_in_a_marked_site()
    {
        var plain = "// raw-sql: planted\nvar sql = \"select 1 from accounts.\"\"Accounts\"\";\";";
        var escaped = "// raw-sql: planted\nvar sql = \"select 1 from accounts.\\\"Accounts\\\"\";";
        var foreign = new[] { "accounts", "sites" };

        Assert.Equal(["accounts"], ModuleRawSqlScanner.ForeignReferences(plain, foreign));
        Assert.Equal(["accounts"], ModuleRawSqlScanner.ForeignReferences(escaped, foreign));
    }

    /// <summary>
    /// The scanner's accepting controls: a module's own schema is not foreign, a variable that happens
    /// to share a schema's name is not a table, and an unmarked file is not a raw-SQL site.
    /// </summary>
    [Fact]
    public void The_scanner_accepts_what_is_not_a_foreign_table()
    {
        var foreign = new[] { "accounts", "sites" };
        var ownSchemaOnly = "// raw-sql: planted\nvar sql = \"select 1 from planted.\\\"Things\\\"\";";
        var variable = "// raw-sql: planted\nvar rows = sites.Where(site => site.Active);";
        var unmarked = "var sql = \"select 1 from accounts.\\\"Accounts\\\"\";";

        Assert.Empty(ModuleRawSqlScanner.ForeignReferences(ownSchemaOnly, foreign));
        Assert.Empty(ModuleRawSqlScanner.ForeignReferences(variable, foreign));
        Assert.Empty(ModuleRawSqlScanner.ForeignReferences(unmarked, foreign));
    }

    /// <summary>No module's raw-SQL site names another module's schema.</summary>
    [Fact]
    public void No_module_raw_sql_site_names_another_modules_schema()
    {
        var modules = ComposedAndUnregisteredModules();
        var schemas = modules
            .Select(module => { return module.Manifest.Id; })
            .ToHashSet(StringComparer.Ordinal);
        var found = new List<string>();
        var examined = 0;

        foreach (var module in modules)
        {
            var folder = ModuleFolder(module);
            Assert.True(Directory.Exists(folder), $"The source folder of module '{module.Manifest.Id}' was not found at {folder}.");
            examined++;

            var foreign = schemas.Where(schema => { return schema != module.Manifest.Id; }).ToList();
            found.AddRange(ModuleRawSqlScanner.Scan(folder, foreign)
                .Select(line => { return $"{module.Manifest.Id}: {line}"; }));
        }

        Assert.True(examined > 0, "No module folder was scanned.");
        Assert.True(
            found.Count == 0,
            "A raw-SQL site names a table in a schema its module does not own (rules/architecture.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, found));
    }

    /// <summary>Judges every composed module's real contexts against its real manifest.</summary>
    /// <returns>All violations, with the module id leading each.</returns>
    private static List<string> RealViolations()
    {
        var modules = ComposedAndUnregisteredModules();
        var contexts = ModuleDbContexts.CreateFor(modules)
            .ToLookup(context => { return context.GetType().Assembly; });
        var violations = new List<string>();

        foreach (var module in modules)
        {
            var assembly = module.GetType().Assembly;
            var implementsMigrations = assembly.GetTypes().Any(type =>
            {
                return typeof(IModuleMigrations).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract;
            });

            violations.AddRange(ModuleSchemaLaw.Violations(
                module.Manifest.Id,
                module.Manifest.SchemaName,
                contexts[assembly].ToList(),
                implementsMigrations));
        }

        return violations;
    }

    /// <summary>The registry's modules and the ones built and not yet composed, from the one list.</summary>
    /// <returns><c>ModuleRegistry.All</c> followed by <c>ModuleContractCensus.UnregisteredModules</c>.</returns>
    private static List<IPanelModule> ComposedAndUnregisteredModules()
    {
        return ModuleRegistry.All.Concat(ModuleContractCensus.UnregisteredModules()).ToList();
    }

    /// <summary>Locates the source folder of a module from its assembly name.</summary>
    /// <param name="module">The module whose folder is wanted.</param>
    /// <returns>Absolute path of <c>backend/src/Maran.Modules/&lt;Name&gt;</c>.</returns>
    private static string ModuleFolder(IPanelModule module)
    {
        var assemblyName = module.GetType().Assembly.GetName().Name!;
        var folderName = assemblyName[AssemblyPrefix.Length..];
        return Path.Combine(ResourceTriples.BackendRoot(), "src", "Maran.Modules", folderName);
    }

    /// <summary>Whether a violation list carries a given rule.</summary>
    /// <param name="violations">What the law reported.</param>
    /// <param name="rule">The rule's constant on <see cref="ModuleSchemaLaw"/>.</param>
    /// <returns><c>true</c> when some line begins with the rule.</returns>
    private static bool Flags(IReadOnlyList<string> violations, string rule)
    {
        return violations.Any(line => { return line.StartsWith(rule, StringComparison.Ordinal); });
    }

    /// <summary>Renders what the law reported, for an assertion message.</summary>
    /// <param name="violations">What the law reported.</param>
    /// <returns>The lines, or a word saying there were none.</returns>
    private static string Describe(IReadOnlyList<string> violations)
    {
        return violations.Count == 0 ? "The law reported nothing." : "The law reported: " + string.Join("; ", violations);
    }
}
