using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence.Configurations;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Tasks.Persistence;

/// <summary>
/// The Tasks module's only database context. Owns the <c>tasks</c> PostgreSQL schema exclusively —
/// no other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// The query filter below is THE authorisation mechanism of this module's reads. An administrator
/// reads every task. A customer reads exactly the tasks whose audience is
/// <see cref="TaskAudience.AccountOwner"/> AND whose account is their own — their backups, restores
/// and certificates — and nothing else. A suspension of their own account carries their account id
/// and is still not in their result set, because its audience is administrator-only; another
/// tenant's task and server-wide work are not either.
/// </para>
/// <para>
/// It is expressed here, in the model, rather than as a check each handler remembers, for exactly
/// the reason every tenant filter is: a handler that forgets a <c>Where</c> clause still cannot leak,
/// and another tenant's task answers 404 rather than 403 — the row genuinely is not in the result
/// set, so nothing about it is confirmed (spec §8, rules/testing.md item 3).
/// </para>
/// <para>
/// What a customer sees OF their own task is a separate decision, taken where a row becomes a wire
/// shape: the log is operator material and is withheld (<c>PanelTaskMapper</c>).
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

    /// <summary>Applies the schema, the entity configuration, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new PanelTaskConfiguration());

        // Physically, not by a handler remembering to check. Both halves are needed: the audience
        // says the task is the owner's to read, the account says whose. A null account id never
        // matches, even for a principal whose token names no account.
        modelBuilder.Entity<PanelTask>().HasQueryFilter(task =>
            _currentUser.IsAdmin
            || (task.Audience == TaskAudience.AccountOwner
                && task.AccountId != null
                && task.AccountId == _currentUser.AccountId));

        base.OnModelCreating(modelBuilder);
    }
}
