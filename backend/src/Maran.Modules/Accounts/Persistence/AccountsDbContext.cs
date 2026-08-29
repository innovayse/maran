using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence.Configurations;

namespace Maran.Modules.Accounts.Persistence;

/// <summary>
/// The Accounts module's only database context. Owns the <c>accounts</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another
/// module's schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
public sealed class AccountsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "accounts";

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
        : base(options)
    {
    }

    /// <summary>The hosting accounts owned by this module.</summary>
    public DbSet<Account> Accounts
    {
        get
        {
            return Set<Account>();
        }
    }

    /// <summary>The plans accounts are created against.</summary>
    public DbSet<Plan> Plans
    {
        get
        {
            return Set<Plan>();
        }
    }

    /// <summary>Applies the schema and every entity configuration for this module.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
