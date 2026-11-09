using Maran.Modules.Fleet.Domain.Entities;
using Maran.Modules.Fleet.Persistence.Configurations;

namespace Maran.Modules.Fleet.Persistence;

/// <summary>
/// The Fleet module's only database context. Owns the <c>fleet</c> PostgreSQL schema exclusively — no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// It carries no tenant query filter and takes no <see cref="ICurrentUser"/>, deliberately: a server is
/// the installation's, not a customer's. No row has an owner for a filter to close over, every path to
/// the registry is administrator-only, and a customer never names a server (an account's placement is
/// read from the account's own row). Because <see cref="Server"/> carries no <c>AccountId</c>,
/// <c>TenantScopeTests</c> does not ask it for a filter it must not have.
/// </remarks>
public sealed class FleetDbContext : DbContext
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "fleet";

    /// <summary>Creates the context.</summary>
    /// <param name="options">The provider and connection, from the module's registration.</param>
    public FleetDbContext(DbContextOptions<FleetDbContext> options)
        : base(options)
    {
    }

    /// <summary>The servers the panel manages: its own host and every enrolled node.</summary>
    public DbSet<Server> Servers
    {
        get
        {
            return Set<Server>();
        }
    }

    /// <summary>Applies the schema and the entity configuration.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new ServerConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
