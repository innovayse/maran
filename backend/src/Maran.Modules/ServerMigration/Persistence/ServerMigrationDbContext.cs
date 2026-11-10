using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Persistence.Configurations;

namespace Maran.Modules.ServerMigration.Persistence;

/// <summary>
/// The ServerMigration module's only database context. Owns the <c>servermigration</c> PostgreSQL schema exclusively: no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// It carries no tenant query filter and takes no <see cref="ICurrentUser"/>, deliberately: a remote server and
/// everything discovered on it are the installation's bookkeeping, not a customer's. No row carries an
/// <c>AccountId</c> (or a reseller id) for a filter to close over, no row reaches a required relationship to one,
/// and every path to them is administrator-only, so <c>TenantScopeTests</c> does not see these entities and asks for
/// no filter. The panel's private key for each server is encrypted at rest through the cipher this context is given.
/// </remarks>
public sealed class ServerMigrationDbContext : DbContext
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "servermigration";

    /// <summary>The cipher the private key column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the context.</summary>
    /// <param name="options">The provider and connection, from the module's registration.</param>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public ServerMigrationDbContext(DbContextOptions<ServerMigrationDbContext> options, IEncryptionService encryptionService)
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>The servers registered as sources.</summary>
    public DbSet<RemoteServer> RemoteServers
    {
        get
        {
            return Set<RemoteServer>();
        }
    }

    /// <summary>The host keys the servers offered at their last scan.</summary>
    public DbSet<RemoteServerHostKeyCandidate> RemoteServerHostKeyCandidates
    {
        get
        {
            return Set<RemoteServerHostKeyCandidate>();
        }
    }

    /// <summary>The accounts the last discovery found on each server.</summary>
    public DbSet<DiscoveredAccount> DiscoveredAccounts
    {
        get
        {
            return Set<DiscoveredAccount>();
        }
    }

    /// <summary>The sites of those accounts.</summary>
    public DbSet<DiscoveredSite> DiscoveredSites
    {
        get
        {
            return Set<DiscoveredSite>();
        }
    }

    /// <summary>The databases of those accounts.</summary>
    public DbSet<DiscoveredDatabase> DiscoveredDatabases
    {
        get
        {
            return Set<DiscoveredDatabase>();
        }
    }

    /// <summary>What the last discovery of each server could not read or report in full.</summary>
    public DbSet<DiscoveryWarning> DiscoveryWarnings
    {
        get
        {
            return Set<DiscoveryWarning>();
        }
    }

    /// <summary>The key this server signs its exports with; at most one row.</summary>
    public DbSet<MigrationSigningKey> MigrationSigningKeys
    {
        get
        {
            return Set<MigrationSigningKey>();
        }
    }

    /// <summary>The accounts exported for another server.</summary>
    public DbSet<AccountExport> AccountExports
    {
        get
        {
            return Set<AccountExport>();
        }
    }

    /// <summary>The single-use grants that let another panel pull an export.</summary>
    public DbSet<ExportGrant> ExportGrants
    {
        get
        {
            return Set<ExportGrant>();
        }
    }

    /// <summary>The accounts being, or having been, imported from a package.</summary>
    public DbSet<AccountImport> AccountImports
    {
        get
        {
            return Set<AccountImport>();
        }
    }

    /// <summary>The whole-server migrations (issue #197, phase 2).</summary>
    public DbSet<MigrationJob> MigrationJobs
    {
        get
        {
            return Set<MigrationJob>();
        }
    }

    /// <summary>The source accounts of those migrations, with their mapping and progress.</summary>
    public DbSet<MigrationJobAccount> MigrationJobAccounts
    {
        get
        {
            return Set<MigrationJobAccount>();
        }
    }

    /// <summary>Applies the schema and the entity configuration.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new RemoteServerConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new RemoteServerHostKeyCandidateConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveredAccountConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveredSiteConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveredDatabaseConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryWarningConfiguration());
        modelBuilder.ApplyConfiguration(new MigrationSigningKeyConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new AccountExportConfiguration());
        modelBuilder.ApplyConfiguration(new ExportGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AccountImportConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new MigrationJobConfiguration());
        modelBuilder.ApplyConfiguration(new MigrationJobAccountConfiguration(_encryptionService));

        base.OnModelCreating(modelBuilder);
    }
}
