using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Persistence.Configurations;

namespace Maran.Modules.Sites.Persistence;

/// <summary>
/// The Sites module's only database context. Owns the <c>sites</c> PostgreSQL schema exclusively —
/// no other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// This is the product's first tenant-scoped context, and the shape every later one copies: the
/// context takes <see cref="ICurrentUser"/> and closes a global query filter over it, so a
/// customer's rows are separated from another customer's by the QUERY the provider emits rather
/// than by each handler remembering a <c>Where</c> clause (spec §8).
/// </remarks>
public sealed class SitesDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "sites";

    /// <summary>The authenticated principal whose tenant scope every query is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SitesDbContext(DbContextOptions<SitesDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The sites owned by this module, already scoped to the current user's tenant.</summary>
    public DbSet<Site> Sites
    {
        get
        {
            return Set<Site>();
        }
    }

    /// <summary>Applies the schema, the entity configurations, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new SiteConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries
        // their account id and the filter closes over it, so a query that forgets a Where clause
        // still cannot leak. It is also why another tenant's site answers 404 rather than 403 —
        // the row is not found, so there is nothing whose existence a probe could confirm.
        modelBuilder.Entity<Site>().HasQueryFilter(site =>
            _currentUser.IsAdmin || site.AccountId == _currentUser.AccountId);

        base.OnModelCreating(modelBuilder);
    }
}
