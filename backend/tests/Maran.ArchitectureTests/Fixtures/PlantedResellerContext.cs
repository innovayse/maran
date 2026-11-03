using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// A context holding one reseller-owned entity with a query filter and one without, so the
/// reseller axis of the tenant census has a violation to refuse and a clean case to accept.
/// </summary>
/// <remarks>
/// Never composed by the panel and never reached through <c>ModuleRegistry</c>; it is built only by the
/// tests that need a planted model. The provider is configured and the connection never used, exactly
/// as <see cref="ModuleDbContexts"/> builds the real ones.
/// </remarks>
public sealed class PlantedResellerContext : DbContext
{
    /// <summary>The planted entity the context filters.</summary>
    public DbSet<PlantedFilteredResellerRow> Filtered
    {
        get
        {
            return Set<PlantedFilteredResellerRow>();
        }
    }

    /// <summary>The planted entity the context leaves unfiltered.</summary>
    public DbSet<PlantedUnfilteredResellerRow> Unfiltered
    {
        get
        {
            return Set<PlantedUnfilteredResellerRow>();
        }
    }

    /// <summary>Creates the context.</summary>
    /// <param name="options">Options carrying the provider; the connection string is never used.</param>
    public PlantedResellerContext(DbContextOptions<PlantedResellerContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlantedFilteredResellerRow>().HasQueryFilter(row => row.ResellerId == null);
        modelBuilder.Entity<PlantedUnfilteredResellerRow>();

        base.OnModelCreating(modelBuilder);
    }
}
