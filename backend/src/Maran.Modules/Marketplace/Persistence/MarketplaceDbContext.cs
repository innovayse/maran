using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Persistence.Configurations;

namespace Maran.Modules.Marketplace.Persistence;

/// <summary>
/// The Marketplace module's only database context. Owns the <c>marketplace</c> PostgreSQL schema
/// exclusively: no other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <b>No entity here is tenant-owned, so there is no query filter and no <c>ICurrentUser</c>.</b> The
/// marketplace is one installation-wide facility an administrator drives: the switch, the history of the
/// packages it staged, the highest version seen per module and the per-module operation claims belong to
/// the server and to no customer. What authorises every path to these rows is the administrator-only
/// policy on the controller. Because no entity carries an <c>AccountId</c>, <c>TenantScopeTests</c> does
/// not ask for a filter that must not exist.
/// </remarks>
public sealed class MarketplaceDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "marketplace";

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    public MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options)
        : base(options)
    {
    }

    /// <summary>The history of the packages the panel staged, oldest first by <c>InstalledAt</c>.</summary>
    public DbSet<InstalledPackage> InstalledPackages
    {
        get
        {
            return Set<InstalledPackage>();
        }
    }

    /// <summary>The installation's marketplace switch: at most one row.</summary>
    public DbSet<MarketplaceSettings> MarketplaceSettings
    {
        get
        {
            return Set<MarketplaceSettings>();
        }
    }

    /// <summary>The highest version staged per module.</summary>
    public DbSet<ModuleVersionFloor> ModuleVersionFloors
    {
        get
        {
            return Set<ModuleVersionFloor>();
        }
    }

    /// <summary>The claims that an operation is running on a module, one per module at most.</summary>
    public DbSet<ModuleOperationLock> ModuleOperationLocks
    {
        get
        {
            return Set<ModuleOperationLock>();
        }
    }

    /// <summary>Applies the schema and the four entity configurations.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new InstalledPackageConfiguration());
        modelBuilder.ApplyConfiguration(new MarketplaceSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new ModuleVersionFloorConfiguration());
        modelBuilder.ApplyConfiguration(new ModuleOperationLockConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
