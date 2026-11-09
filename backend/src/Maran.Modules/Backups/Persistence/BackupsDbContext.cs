using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence.Configurations;
using Maran.SharedKernel.Callers;

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

    /// <summary>The cipher the archive key column is stored under.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="encryptionService">
    /// The panel's shared cipher for secrets at rest, applied to exactly one column: the archive key of
    /// a backup destination.
    /// </param>
    public BackupsDbContext(
        DbContextOptions<BackupsDbContext> options,
        ICurrentUser currentUser,
        IEncryptionService encryptionService)
        : base(options)
    {
        _currentUser = currentUser;
        _encryptionService = encryptionService;
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

    /// <summary>
    /// The accounts left out of the unattended backup schedule, already scoped to the current user's
    /// tenant: a customer sees at most their own account's row, an administrator sees them all.
    /// </summary>
    /// <remarks>
    /// Scoped because the row names an account (<c>TenantScopeTests</c> asks for a filter on every entity
    /// carrying an <c>AccountId</c>). The sweep that must read EVERY row has no principal and bypasses the
    /// filter on purpose, with its reason on the line (<c>BackupRunHandler</c>).
    /// </remarks>
    public DbSet<AccountBackupOptOut> AccountBackupOptOuts
    {
        get
        {
            return Set<AccountBackupOptOut>();
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
    /// schedule they may edit, and the host-wide row names no account so it matches nobody. That
    /// holds for a caller with no account too: the filter requires the row to name an account, so a
    /// null tenant does not match the host-wide row's null (issue #438). Only the administrator arm
    /// reads the host-wide policy, and a handler serving it says so.
    /// </remarks>
    public DbSet<BackupSchedule> BackupSchedules
    {
        get
        {
            return Set<BackupSchedule>();
        }
    }

    /// <summary>
    /// Whether the current principal is a server administrator, whom no tenant filter narrows. Read
    /// from the context at query time, so each request's own caller decides.
    /// </summary>
    private bool IsAdministrator
    {
        get
        {
            return _currentUser.Caller is AdministratorCaller;
        }
    }

    /// <summary>
    /// The account the current principal acts for, or null for an administrator, an anonymous caller
    /// and a customer acting for no account — none of whom owns a row.
    /// </summary>
    private Guid? TenantAccountId
    {
        get
        {
            return _currentUser.Caller is CustomerCaller customer ? customer.AccountId : null;
        }
    }

    /// <summary>Applies the schema, the entity configuration, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new BackupDestinationConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new BackupConfiguration());
        modelBuilder.ApplyConfiguration(new BackupScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new AccountBackupOptOutConfiguration());

        modelBuilder.Entity<Backup>().HasQueryFilter(backup =>
            IsAdministrator || backup.AccountId == TenantAccountId);

        // The host-wide row has a null AccountId, and EF turns `AccountId == <null parameter>` into
        // `IS NULL`, which would show it to every caller with no account. The explicit non-null arm
        // makes "no tenant" match no row; only the administrator arm reaches the host-wide policy.
        modelBuilder.Entity<BackupSchedule>().HasQueryFilter(schedule =>
            IsAdministrator || (schedule.AccountId != null && schedule.AccountId == TenantAccountId));

        modelBuilder.Entity<AccountBackupOptOut>().HasQueryFilter(optOut =>
            IsAdministrator || optOut.AccountId == TenantAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
