using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence.Configurations;

namespace Maran.Modules.Tasks.Persistence;

/// <summary>
/// The Tasks module's only database context. Owns the <c>tasks</c> PostgreSQL schema exclusively —
/// no other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// The query filter below is THE authorisation mechanism of this module's reads, and it is not a
/// tenant filter: it is an administrator filter. Every kind of task recorded in v1 is an
/// administrator's operation — issuing a certificate, renewing one, deleting an account — so a task
/// row names a domain or an account name that a customer has no business learning about, and a
/// customer's read must return nothing at all rather than a filtered subset.
/// </para>
/// <para>
/// It is expressed here, in the model, rather than as a check each handler remembers, for exactly
/// the reason a tenant filter is: a handler that forgets a <c>Where</c> clause still cannot leak,
/// and a task belonging to the administrator's world answers 404 to a customer rather than 403 —
/// the row genuinely is not in the result set, so nothing about it is confirmed
/// (spec §8, rules/testing.md item 3).
/// </para>
/// <para>
/// WRITES are deliberately not filtered, and could not be: <c>TaskRecorder</c> records an
/// unattended renewal pass that has no signed-in caller at all, and a filter on writes would make
/// the recording depend on who happened to be logged in.
/// </para>
/// </remarks>
public sealed class TasksDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "tasks";

    /// <summary>The authenticated principal every read is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public TasksDbContext(DbContextOptions<TasksDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The panel's tasks, already filtered to what the caller may see.</summary>
    public DbSet<PanelTask> PanelTasks
    {
        get
        {
            return Set<PanelTask>();
        }
    }

    /// <summary>Applies the schema, the entity configuration, and the administrator query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new PanelTaskConfiguration());

        // R14: tasks are admin-only in v1. Physically, not by a handler remembering to check.
        modelBuilder.Entity<PanelTask>().HasQueryFilter(task => _currentUser.IsAdmin);

        base.OnModelCreating(modelBuilder);
    }
}
