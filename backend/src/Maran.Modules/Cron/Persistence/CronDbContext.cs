using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Persistence.Configurations;

namespace Maran.Modules.Cron.Persistence;

/// <summary>
/// The Cron module's only database context. Owns the <c>cron</c> PostgreSQL schema exclusively — no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// It holds the runs the panel started by hand (<see cref="CronRun"/>, issue #158) and nothing else.
/// The cron entries themselves are not here and must not arrive: the account's crontab is their only
/// record, and the module reads it through the agent every time (see <see cref="CronModule"/>).
/// </remarks>
public sealed class CronDbContext : DbContext
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "cron";

    /// <summary>The caller whose account the tenant filter closes over.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context.</summary>
    /// <param name="options">The provider and connection, from the module's registration.</param>
    /// <param name="currentUser">The caller, for the tenant filter.</param>
    public CronDbContext(DbContextOptions<CronDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The runs started by hand, one row each.</summary>
    public DbSet<CronRun> CronRuns
    {
        get
        {
            return Set<CronRun>();
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new CronRunConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries
        // their account id and the filter closes over it.
        modelBuilder.Entity<CronRun>().HasQueryFilter(run =>
            _currentUser.IsAdmin || run.AccountId == _currentUser.AccountId);

        base.OnModelCreating(modelBuilder);
    }
}
