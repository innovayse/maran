using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence.Configurations;

namespace Maran.Modules.Backups.Persistence;

/// <summary>
/// The Backups module's only database context. Owns the <c>backups</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another module's
/// schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// Tenant-scoped in the shape the other modules' contexts established: the context takes
/// <see cref="ICurrentUser"/> and closes a global query filter over it, so one customer's backups
/// are separated from another's by the QUERY the provider emits rather than by each handler
/// remembering a <c>Where</c> clause (spec §8). That is also why another tenant's backup answers
/// 404 rather than 403 — the row is not found, so there is nothing whose existence a probe could
/// confirm.
///
/// The filter matters more here than almost anywhere else in the panel: a backup id is the name of
/// an archive containing another customer's files and the contents of their database, so a listing
/// that leaked one would be handing out the identifier a restore and a delete are addressed by.
/// </remarks>
public sealed class BackupsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "backups";

    /// <summary>The authenticated principal whose tenant scope every query is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public BackupsDbContext(DbContextOptions<BackupsDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// The places this server records backups as living. Host-wide configuration, and therefore not
    /// tenant-scoped: the rows carry no account id, name no customer's data, and are read only by an
    /// administrators-only surface.
    /// </summary>
    public DbSet<BackupDestination> BackupDestinations
    {
        get
        {
            return Set<BackupDestination>();
        }
    }

    /// <summary>The backups this module records, already scoped to the current user's tenant.</summary>
    public DbSet<Backup> Backups
    {
        get
        {
            return Set<Backup>();
        }
    }

    /// <summary>
    /// The operator's standing instructions to back accounts up unattended, already scoped to the
    /// current user's tenant.
    /// </summary>
    /// <remarks>
    /// Scoped even though the whole schedules surface is administrators-only, because the filter is
    /// what <c>TenantScopeTests</c> asks for of any entity carrying an <c>AccountId</c> and because
    /// an authorization attribute on a controller is a decision one route can be added without. A
    /// customer who somehow reached a schedule read would see nothing: their own account has no
    /// schedule they may edit, and the host-wide row names no account so it matches nobody.
    /// </remarks>
    public DbSet<BackupSchedule> BackupSchedules
    {
        get
        {
            return Set<BackupSchedule>();
        }
    }

    /// <summary>Applies the schema, the entity configuration, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new BackupDestinationConfiguration());
        modelBuilder.ApplyConfiguration(new BackupConfiguration());
        modelBuilder.ApplyConfiguration(new BackupScheduleConfiguration());

        modelBuilder.Entity<Backup>().HasQueryFilter(backup =>
            _currentUser.IsAdmin || backup.AccountId == _currentUser.AccountId);

        modelBuilder.Entity<BackupSchedule>().HasQueryFilter(schedule =>
            _currentUser.IsAdmin || schedule.AccountId == _currentUser.AccountId);

        base.OnModelCreating(modelBuilder);
    }
}
