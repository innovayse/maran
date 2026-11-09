using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// A context that claims one module's schema as its default and maps its only table into another
/// module's — the planted violation <c>ModuleSchemaOwnershipTests</c> must flag by rule name.
/// </summary>
/// <remarks>
/// It is the shape of a module that reads or writes a table it does not own while its default schema
/// still looks correct, which is the case a check on the default schema alone would pass. Built with
/// the real Npgsql provider and a connection string that is never used, exactly as the real contexts
/// are in <see cref="ModuleDbContexts"/>, so the model the law reads is a real one.
/// </remarks>
public sealed class PlantedForeignSchemaContext : DbContext
{
    /// <summary>The schema this context claims as its own.</summary>
    public const string OwnSchema = "planted";

    /// <summary>The schema it maps its table into, which belongs to another module.</summary>
    public const string ForeignSchema = "accounts";

    /// <summary>Creates the context.</summary>
    /// <param name="options">Options carrying the provider and an unused connection string.</param>
    public PlantedForeignSchemaContext(DbContextOptions<PlantedForeignSchemaContext> options)
        : base(options)
    {
    }

    /// <summary>Creates a context whose model can be read; no connection is ever opened.</summary>
    /// <returns>The planted context.</returns>
    public static PlantedForeignSchemaContext Create()
    {
        var options = new DbContextOptionsBuilder<PlantedForeignSchemaContext>()
            .UseNpgsql("Host=architecture.invalid;Database=maran")
            .Options;
        return new PlantedForeignSchemaContext(options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(OwnSchema);
        modelBuilder.Entity<PlantedForeignSchemaRow>(row =>
        {
            row.ToTable("PlantedRows", ForeignSchema);
            row.HasKey("Id");
        });
    }
}
