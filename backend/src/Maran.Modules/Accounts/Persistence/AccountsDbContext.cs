using System.Diagnostics;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence.Configurations;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Persistence;

/// <summary>
/// The Accounts module's only database context. Owns the <c>accounts</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another
/// module's schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>The filters here only NARROW, and only for a reseller.</b> Accounts, plans and resellers carry
/// a query filter on the owning reseller: a reseller caller sees the rows it owns and no others, so a
/// handler that forgets a <c>Where</c> still cannot return another reseller's row, and the row that
/// is hidden answers exactly like one that does not exist (404, never 403). Every other caller —
/// administrator, customer, and the anonymous caller of unattended work — is NOT narrowed and sees
/// what it saw before resellers existed, because the module scopes those through its controllers'
/// policies and <c>AccountDirectory</c>. Making this context fail closed for them is a separate
/// hardening, and its consequence is stated here so nobody reads this filter as more than it is:
/// unattended code runs with an anonymous caller, is not narrowed by it, and must therefore never
/// take a reseller-controlled id as the whole of its scoping.
/// </para>
/// </remarks>
public sealed class AccountsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "accounts";

    /// <summary>The authenticated principal whose reseller scope the filters close over.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
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

    /// <summary>The resellers: named businesses that own accounts and plans inside an envelope.</summary>
    public DbSet<Reseller> Resellers
    {
        get
        {
            return Set<Reseller>();
        }
    }

    /// <summary>
    /// The one reseller the caller acts for, or null for every other caller, who is not narrowed. Read
    /// per context instance by the filters, which an expression tree cannot express as a type switch.
    /// Every case is named, so a sixth kind of caller fails here instead of being narrowed by accident.
    /// </summary>
    private Guid? ResellerScope
    {
        get
        {
            return _currentUser.Caller switch
            {
                ResellerCaller reseller => reseller.ResellerId,
                AdministratorCaller => null,
                AnonymousCaller => null,
                CustomerCaller => null,
                AccountlessCustomerCaller => null,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: five cases exist."),
            };
        }
    }

    /// <summary>Applies the schema, every entity configuration, and the reseller query filters.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new ResellerConfiguration());

        // Narrowing only (see the type's remarks): a reseller's context shows it the rows it owns.
        // A reseller sees its own row and no other reseller's, so one cannot even learn that another
        // exists through this context.
        modelBuilder.Entity<Account>().HasQueryFilter(account =>
            ResellerScope == null || account.ResellerId == ResellerScope);
        modelBuilder.Entity<Plan>().HasQueryFilter(plan =>
            ResellerScope == null || plan.ResellerId == ResellerScope);
        modelBuilder.Entity<Reseller>().HasQueryFilter(reseller =>
            ResellerScope == null || reseller.Id == ResellerScope);

        base.OnModelCreating(modelBuilder);
    }
}
