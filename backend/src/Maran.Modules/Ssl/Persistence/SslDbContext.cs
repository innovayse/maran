using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Persistence.Configurations;

namespace Maran.Modules.Ssl.Persistence;

/// <summary>
/// The Ssl module's only database context. Owns the <c>ssl</c> PostgreSQL schema exclusively — no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// Tenant-scoped in the shape <c>SitesDbContext</c> established: the context takes
/// <see cref="ICurrentUser"/> and closes a global query filter over it, so a customer's certificates
/// are separated from another customer's by the QUERY the provider emits rather than by each handler
/// remembering a <c>Where</c> clause (spec §8). That is also why another tenant's certificate answers
/// 404 rather than 403 — the row is not found, so there is nothing whose existence a probe could
/// confirm.
///
/// Unattended renewal has no authenticated caller and must see every account's certificates, so it
/// says <c>IgnoreQueryFilters</c> in one named place (<c>CertificateRenewalHandler</c>) rather than being
/// given a fabricated administrator principal — a principal that exists is a principal something else
/// can be resolved with.
/// </remarks>
public sealed class SslDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "ssl";

    /// <summary>The authenticated principal whose tenant scope every query is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The cipher the ACME account key column is encrypted with at rest.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public SslDbContext(
        DbContextOptions<SslDbContext> options,
        ICurrentUser currentUser,
        IEncryptionService encryptionService)
        : base(options)
    {
        _currentUser = currentUser;
        _encryptionService = encryptionService;
    }

    /// <summary>The panel's ACME registrations, one per authority. Server-wide, never tenant-scoped.</summary>
    public DbSet<AcmeAccount> AcmeAccounts
    {
        get
        {
            return Set<AcmeAccount>();
        }
    }

    /// <summary>The certificates owned by this module, already scoped to the current user's tenant.</summary>
    public DbSet<Certificate> Certificates
    {
        get
        {
            return Set<Certificate>();
        }
    }

    /// <summary>Applies the schema, the entity configurations, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new CertificateConfiguration());
        modelBuilder.ApplyConfiguration(new AcmeAccountConfiguration(_encryptionService));

        modelBuilder.Entity<Certificate>().HasQueryFilter(certificate =>
            _currentUser.IsAdmin || certificate.AccountId == _currentUser.AccountId);

        base.OnModelCreating(modelBuilder);
    }
}
