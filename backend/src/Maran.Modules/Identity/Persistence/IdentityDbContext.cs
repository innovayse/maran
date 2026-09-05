using Maran.Modules.Identity.Domain.Entities;
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

    /// <summary>
    /// The open brute-force counting window of every address that has recently had a sign-in
    /// refused. Working state rather than a record: a row appears when an address fails, is removed
    /// when its window is announced as an attack, and is reclaimed once it has closed unannounced.
    /// The permanent record of what happened is <see cref="AuditEvents"/>.
    /// </summary>
    public DbSet<FailedLoginByIp> FailedLoginsByIp
    {
        get
        {
            return Set<FailedLoginByIp>();
        }
    }

    /// <summary>
    /// The panel's security policy: at most one row, keyed by a constant. See
    /// <see cref="Domain.Entities.SecurityPolicy"/> for why it is a singleton by construction rather than by
    /// convention, and why its absence means the defaults rather than "no policy".
    /// </summary>
    public DbSet<SecurityPolicy> SecurityPolicies
    {
        get
        {
            return Set<SecurityPolicy>();
        }
    }

    /// <summary>
    /// Outstanding permissions to set a password without knowing the old one. Digests only — the
    /// token itself lives in one request and one e-mail and is never written down.
    /// </summary>
    public DbSet<PasswordResetToken> PasswordResetTokens
    {
        get
        {
            return Set<PasswordResetToken>();
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
        modelBuilder.ApplyConfiguration(new FailedLoginByIpConfiguration());
        modelBuilder.ApplyConfiguration(new SecurityPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
