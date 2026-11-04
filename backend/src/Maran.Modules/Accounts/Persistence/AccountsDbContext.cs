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
/// <b>The filters fail closed: a caller sees only what its kind is entitled to, and a kind that is
/// not named sees nothing.</b> Accounts, plans and resellers each carry a query filter with one
/// decision per kind of caller. An administrator sees every row. A reseller sees the rows it owns —
/// its accounts, its plans and its own reseller row, and no other reseller's, so one cannot even
/// learn that another exists through this context. A customer sees the one account it acts for and
/// that account's plan, and no reseller. The anonymous caller and a customer login acting for no
/// account see nothing. A handler that forgets a <c>Where</c> therefore cannot return another
/// tenant's row, and the row that is hidden answers exactly like one that does not exist (404, never
/// 403).
/// </para>
/// <para>
/// <b>The bypass rule.</b> The filters are not the only scoping, but they are the floor: a read that
/// legitimately needs rows its caller may not see says so explicitly with
/// <c>IgnoreQueryFilters()</c>, which is a banned symbol in production code, so each bypass is an
/// <c>RS0030</c> suppression carrying its reason on the line (rules/security.md item 6). Nothing
/// widens a filter implicitly.
/// </para>
/// <para>
/// <b>The consequence for unattended code.</b> A backup run, a retention sweep, a start-up
/// reconciler, a seeder or an event handler for another module runs with the anonymous caller, which
/// this context narrows to nothing. Such code that needs accounts or plans without a principal MUST
/// bypass the filters in the open, and because the bypass then returns every tenant's rows it must
/// never take a tenant-controlled id as the whole of its scoping. Code that forgets to bypass does
/// not leak: it finds nothing.
/// </para>
/// </remarks>
public sealed class AccountsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "accounts";

    /// <summary>The authenticated principal whose kind the filters close over.</summary>
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
    /// Whether the caller may see every account, plan and reseller: true only for an administrator.
    /// Read per context instance by the filters, which an expression tree cannot express as a type
    /// switch. Every case is named, so a sixth kind of caller fails here instead of being granted or
    /// denied by accident.
    /// </summary>
    private bool SeesEveryRow
    {
        get
        {
            return _currentUser.Caller switch
            {
                AdministratorCaller => true,
                ResellerCaller => false,
                CustomerCaller => false,
                AnonymousCaller => false,
                AccountlessCustomerCaller => false,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: five cases exist."),
            };
        }
    }

    /// <summary>
    /// The one reseller the caller acts for, or null when the caller is not a reseller (which narrows
    /// to nothing here, never to everything: every filter checks for null before it compares).
    /// </summary>
    private Guid? ResellerScope
    {
        get
        {
            return _currentUser.Caller switch
            {
                ResellerCaller reseller => reseller.ResellerId,
                AdministratorCaller => null,
                CustomerCaller => null,
                AnonymousCaller => null,
                AccountlessCustomerCaller => null,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: five cases exist."),
            };
        }
    }

    /// <summary>
    /// The one account a customer caller acts for, or null when the caller is not a customer with an
    /// account. Null narrows to nothing: every filter checks for it before it compares.
    /// </summary>
    private Guid? CustomerAccountScope
    {
        get
        {
            return _currentUser.Caller switch
            {
                CustomerCaller customer => customer.AccountId,
                AdministratorCaller => null,
                ResellerCaller => null,
                AnonymousCaller => null,
                AccountlessCustomerCaller => null,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: five cases exist."),
            };
        }
    }

    /// <summary>Applies the schema, every entity configuration, and the fail-closed query filters.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new ResellerConfiguration());

        // Fail closed (see the type's remarks). Every scope is compared only after a null check: EF
        // compares a nullable column with a null parameter as "both null", which would hand a caller
        // with no scope every row that has no owner, so null must mean nothing and never match.
        modelBuilder.Entity<Account>().HasQueryFilter(account =>
            SeesEveryRow
            || (ResellerScope != null && account.ResellerId == ResellerScope)
            || (CustomerAccountScope != null && account.Id == CustomerAccountScope));

        // Account carries only the scalar PlanId (no navigation), so a customer's plan is "a plan that
        // the customer's own account names", written as a subquery over the accounts set. That subquery
        // is itself narrowed by the Account filter above, which agrees with the explicit comparison.
        modelBuilder.Entity<Plan>().HasQueryFilter(plan =>
            SeesEveryRow
            || (ResellerScope != null && plan.ResellerId == ResellerScope)
            || (CustomerAccountScope != null
                && Set<Account>().Any(account => account.Id == CustomerAccountScope && account.PlanId == plan.Id)));

        // A customer is never shown a reseller row, so only the administrator and the reseller itself match.
        modelBuilder.Entity<Reseller>().HasQueryFilter(reseller =>
            SeesEveryRow || (ResellerScope != null && reseller.Id == ResellerScope));

        base.OnModelCreating(modelBuilder);
    }
}
