using System.Diagnostics;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Persistence.Configurations;
using Maran.SharedKernel.Callers;

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

    /// <summary>
    /// True when the caller is an administrator, who sees every account's runs. Read per context
    /// instance by the tenant filter, which an expression tree cannot express as a type switch.
    /// </summary>
    private bool SeesEveryAccount
    {
        get
        {
            return _currentUser.Caller switch
            {
                AdministratorCaller => true,
                ApiKeyCaller => false,
                AnonymousCaller => false,
                CustomerCaller => false,
                AccountlessCustomerCaller => false,
                ResellerCaller => false,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
            };
        }
    }

    /// <summary>
    /// The one account a customer acts for, or null for an administrator, an anonymous caller and a
    /// customer login with no account selected — none of whom own a row, so the filter shows them none
    /// through this arm.
    /// </summary>
    private Guid? OwnAccountId
    {
        get
        {
            return _currentUser.Caller switch
            {
                CustomerCaller customer => customer.AccountId,
                AdministratorCaller => null,
                ApiKeyCaller => null,
                AnonymousCaller => null,
                AccountlessCustomerCaller => null,
                ResellerCaller => null,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
            };
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
            SeesEveryAccount || run.AccountId == OwnAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
