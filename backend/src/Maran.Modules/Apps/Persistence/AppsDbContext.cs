using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Persistence.Configurations;

namespace Maran.Modules.Apps.Persistence;

/// <summary>
/// The Apps module's only database context. Owns the <c>apps</c> PostgreSQL schema exclusively - no other
/// module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <b>There is no tenant query filter here, and there must not be one.</b> The apps on a server belong to
/// the SERVER, the whole write surface is administrator-only, and the start-up reconciler runs with no
/// request and no principal at all: a filter closing over the caller would have nothing to compare
/// against and would hide every row from it.
/// </remarks>
public sealed class AppsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "apps";

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    public AppsDbContext(DbContextOptions<AppsDbContext> options)
        : base(options)
    {
    }

    /// <summary>The apps the panel has installed, is installing or removing, or found on the host.</summary>
    public DbSet<InstalledApp> InstalledApps
    {
        get
        {
            return Set<InstalledApp>();
        }
    }

    /// <summary>Applies the schema and the entity configurations.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new InstalledAppConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
