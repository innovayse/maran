using Maran.Host.Cli.Services;

namespace Maran.Host.Tests.Cli.Services;

/// <summary>
/// Behavioral contract of <see cref="ModuleMigrationCoverage"/>: the registered contexts and the
/// modules' migrations must describe the same schemas, in both directions.
/// </summary>
public sealed class ModuleMigrationCoverageTests
{
    /// <summary>Contexts and migrations covering the same schemas are accepted (accepting control).</summary>
    [Fact]
    public void Contexts_and_migrations_covering_the_same_schemas_are_accepted()
    {
        var contexts = new Dictionary<string, string?> { ["A.SalesContext"] = "sales", ["B.StockContext"] = "stock" };

        ModuleMigrationCoverage.Verify(contexts, ["stock", "sales"]);
    }

    /// <summary>A context no module migrates is refused, naming the context and its schema.</summary>
    [Fact]
    public void A_context_no_module_migrates_is_refused()
    {
        var contexts = new Dictionary<string, string?> { ["A.SalesContext"] = "sales", ["B.StockContext"] = "stock" };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleMigrationCoverage.Verify(contexts, ["sales"]);
        });

        Assert.Contains("B.StockContext (schema 'stock') has no IModuleMigrations", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Migrations for a schema no context maps are refused, so a success line cannot claim work that never ran.</summary>
    [Fact]
    public void Migrations_for_a_schema_no_context_maps_are_refused()
    {
        var contexts = new Dictionary<string, string?> { ["A.SalesContext"] = "sales" };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleMigrationCoverage.Verify(contexts, ["sales", "ghost"]);
        });

        Assert.Contains("schema 'ghost' matches no registered context", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A context that declares no default schema cannot be matched and is refused.</summary>
    [Fact]
    public void A_context_with_no_default_schema_is_refused()
    {
        var contexts = new Dictionary<string, string?> { ["A.SalesContext"] = null };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleMigrationCoverage.Verify(contexts, []);
        });

        Assert.Contains("A.SalesContext declares no default schema", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Two migrations for one schema are refused, since either could be the one that runs.</summary>
    [Fact]
    public void Two_migrations_for_one_schema_are_refused()
    {
        var contexts = new Dictionary<string, string?> { ["A.SalesContext"] = "sales" };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleMigrationCoverage.Verify(contexts, ["sales", "sales"]);
        });

        Assert.Contains("schema 'sales' is migrated by more than one IModuleMigrations", failure.Message, StringComparison.Ordinal);
    }
}
