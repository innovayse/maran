using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Persistence.Configurations;

namespace Maran.Modules.Identity.Persistence;

/// <summary>
/// The Identity module's only database context. Owns the <c>identity</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another
/// module's schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
public sealed class IdentityDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "identity";

    /// <summary>The cipher protecting columns that are secret at rest, chiefly the TOTP secret.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="encryptionService">The cipher applied to secret columns through <c>EncryptedStringConverter</c>.</param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IEncryptionService encryptionService)
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>The panel logins owned by this module.</summary>
    public DbSet<User> Users
    {
        get
        {
            return Set<User>();
        }
    }

    /// <summary>The refresh-token sessions of those users.</summary>
    public DbSet<Session> Sessions
    {
        get
        {
            return Set<Session>();
        }
    }

    /// <summary>The unused and spent two-factor recovery codes.</summary>
    public DbSet<RecoveryCode> RecoveryCodes
    {
        get
        {
            return Set<RecoveryCode>();
        }
    }

    /// <summary>The append-only audit journal.</summary>
    public DbSet<AuditEvent> AuditEvents
    {
        get
        {
            return Set<AuditEvent>();
        }
    }

    /// <summary>Applies the schema and every entity configuration for this module.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new UserConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new SessionConfiguration());
        modelBuilder.ApplyConfiguration(new RecoveryCodeConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
