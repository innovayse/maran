using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Persistence.Configurations;

namespace Maran.Modules.Ftp.Persistence;

/// <summary>
/// The Ftp module's only database context. Owns the <c>ftp</c> PostgreSQL schema exclusively — no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>Two entities, and exactly one of them is filtered. That asymmetry is the whole design of this
/// context and is the first thing to read.</b>
/// </para>
/// <para>
/// <see cref="FtpsSettings"/> is a single installation-wide row that belongs to the server and to no
/// customer. A tenant filter over a row with no owner would have to invent an owner, and an invented
/// tenant scope is worse than none: it reads as protection while protecting nothing. What authorises
/// every path to it is the <c>AdminOnly</c> policy on <c>FtpsServerController</c>, and because the
/// entity carries no <c>AccountId</c> the panel's <c>TenantScopeTests</c> does not ask it for a
/// filter it must not have.
/// </para>
/// <para>
/// <see cref="FtpUsers"/> is the opposite in every respect. Its rows belong to customers, and the
/// filter below is not one protection among several: it is THE authorisation mechanism of the
/// customer-facing half of this module. The host has no notion of a tenant, so nothing the agent can
/// be asked answers "may this customer see, delete or re-credential this login" — the panel's own
/// rows are the only record of who asked for what. A handler that forgot a <c>Where</c> clause still
/// cannot leak, and another tenant's login answers 404 rather than 403 because the row genuinely is
/// not in the result set (rules/security.md item 6).
/// </para>
/// <para>
/// The context therefore takes an <see cref="ICurrentUser"/>, which it did not before this entity
/// existed. An earlier revision recorded that it needed none and that the design-time factory had no
/// principal to fabricate; that was true of a context holding only the settings row and stopped
/// being true the moment a row carrying an <c>AccountId</c> joined it.
/// </para>
/// </remarks>
public sealed class FtpDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "ftp";

    /// <summary>The authenticated principal whose tenant scope the login rows are filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public FtpDbContext(DbContextOptions<FtpDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The installation's FTPS settings: at most one row, keyed by a fixed identity.</summary>
    public DbSet<FtpsSettings> FtpsSettings
    {
        get
        {
            return Set<FtpsSettings>();
        }
    }

    /// <summary>The customer FTPS logins this module owns, already scoped to the current user's tenant.</summary>
    public DbSet<FtpUser> FtpUsers
    {
        get
        {
            return Set<FtpUser>();
        }
    }

    /// <summary>Applies the schema, both entity configurations, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new FtpsSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new FtpUserConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries
        // their account id and the filter closes over it.
        modelBuilder.Entity<FtpUser>().HasQueryFilter(ftpUser =>
            _currentUser.IsAdmin || ftpUser.AccountId == _currentUser.AccountId);

        base.OnModelCreating(modelBuilder);
    }
}
