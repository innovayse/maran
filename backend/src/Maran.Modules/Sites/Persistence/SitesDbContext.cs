using System.Diagnostics;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence.Configurations;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Sites.Persistence;

/// <summary>
/// The Sites module's only database context. Owns the <c>sites</c> PostgreSQL schema exclusively —
/// no other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// This is the product's first tenant-scoped context, and the shape every later one copies: the
/// context takes <see cref="ICurrentUser"/> and closes a global query filter over it, so a
/// customer's rows are separated from another customer's by the QUERY the provider emits rather
/// than by each handler remembering a <c>Where</c> clause (spec §8).
/// </remarks>
public sealed class SitesDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "sites";

    /// <summary>The authenticated principal whose tenant scope every query is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SitesDbContext(DbContextOptions<SitesDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The sites owned by this module, already scoped to the current user's tenant.</summary>
    public DbSet<Site> Sites
    {
        get
        {
            return Set<Site>();
        }
    }

    /// <summary>
    /// The hostnames claimed by this account's sites — one row per domain and per alias.
    /// </summary>
    /// <remarks>
    /// Tenant-scoped like <see cref="Sites"/>, so a customer cannot enumerate the names another
    /// customer serves. The one read that must see every account's rows — "is this name already
    /// claimed anywhere on the server" — says <c>IgnoreQueryFilters</c> out loud where it is made.
    /// </remarks>
    public DbSet<SiteHostname> SiteHostnames
    {
        get
        {
            return Set<SiteHostname>();
        }
    }

    /// <summary>The per-site options of this account's sites, scoped through the site they belong to.</summary>
    public DbSet<SiteOptions> SiteOptions
    {
        get
        {
            return Set<SiteOptions>();
        }
    }

    /// <summary>The per-day traffic figures of this account's sites.</summary>
    /// <remarks>
    /// Tenant-scoped through the owning site, like <see cref="SiteHostnames"/>. The unattended hourly pass
    /// has no principal and reads and writes them with the filter lifted, saying so where it does.
    /// </remarks>
    public DbSet<SiteTrafficDay> SiteTrafficDays
    {
        get
        {
            return Set<SiteTrafficDay>();
        }
    }

    /// <summary>Where the last read of each of this account's sites' access logs stopped.</summary>
    /// <remarks>Tenant-scoped through the owning site, exactly as <see cref="SiteTrafficDays"/> is.</remarks>
    public DbSet<SiteTrafficCursor> SiteTrafficCursors
    {
        get
        {
            return Set<SiteTrafficCursor>();
        }
    }

    /// <summary>
    /// The PHP settings customers chose for their accounts (issue #138), one row per setting per account.
    /// </summary>
    /// <remarks>
    /// Tenant-scoped like <see cref="Sites"/>. The pool writers that run for somebody else — an
    /// administrator creating a site, the start-up pass, a plan edit — read them through
    /// <c>PhpAccountPoolSettings</c>, which says <c>IgnoreQueryFilters</c> out loud where it does.
    /// </remarks>
    public DbSet<PhpSetting> PhpSettings
    {
        get
        {
            return Set<PhpSetting>();
        }
    }

    /// <summary>
    /// True when the caller is an administrator, who sees every account's rows. Read per context
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

    /// <summary>Applies the schema, the entity configurations, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new SiteConfiguration());
        modelBuilder.ApplyConfiguration(new SiteHostnameConfiguration());
        modelBuilder.ApplyConfiguration(new PhpSettingConfiguration());
        modelBuilder.ApplyConfiguration(new SiteTrafficDayConfiguration());
        modelBuilder.ApplyConfiguration(new SiteTrafficCursorConfiguration());
        modelBuilder.ApplyConfiguration(new SiteOptionsConfiguration());
        modelBuilder.ApplyConfiguration(new SiteRedirectConfiguration());
        modelBuilder.ApplyConfiguration(new SiteBasicAuthUserConfiguration());
        modelBuilder.ApplyConfiguration(new SiteIpRuleConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries
        // their account id and the filter closes over it, so a query that forgets a Where clause
        // still cannot leak. It is also why another tenant's site answers 404 rather than 403 —
        // the row is not found, so there is nothing whose existence a probe could confirm.
        modelBuilder.Entity<Site>().HasQueryFilter(site =>
            SeesEveryAccount || site.AccountId == OwnAccountId);

        // A claim belongs to whoever owns the site making it, so its scope is read through the
        // required relationship rather than from a second copy of the account id that could
        // disagree with the site's own (rules/security.md item 6: every tenant entity is filtered).
        // A customer's PHP settings carry their own account id, so the filter reads it directly.
        modelBuilder.Entity<PhpSetting>().HasQueryFilter(setting =>
            SeesEveryAccount || setting.AccountId == OwnAccountId);

        modelBuilder.Entity<SiteHostname>().HasQueryFilter(hostname =>
            SeesEveryAccount || hostname.Site.AccountId == OwnAccountId);

        // The options and their rows are scoped through the site, then through the options, for the
        // same reason: none carries an account id of its own that could disagree with the site's. A
        // child without its own filter would be a row a customer could read by id.
        modelBuilder.Entity<SiteOptions>().HasQueryFilter(options =>
            SeesEveryAccount || options.Site.AccountId == OwnAccountId);
        modelBuilder.Entity<SiteRedirect>().HasQueryFilter(redirect =>
            SeesEveryAccount || redirect.Options.Site.AccountId == OwnAccountId);
        modelBuilder.Entity<SiteBasicAuthUser>().HasQueryFilter(user =>
            SeesEveryAccount || user.Options.Site.AccountId == OwnAccountId);
        modelBuilder.Entity<SiteIpRule>().HasQueryFilter(rule =>
            SeesEveryAccount || rule.Options.Site.AccountId == OwnAccountId);

        // Traffic figures and the read position belong to whoever owns the site, and are scoped the same
        // way: through the required relationship, never by a second copy of the account id.
        modelBuilder.Entity<SiteTrafficDay>().HasQueryFilter(day =>
            SeesEveryAccount || day.Site.AccountId == OwnAccountId);

        modelBuilder.Entity<SiteTrafficCursor>().HasQueryFilter(cursor =>
            SeesEveryAccount || cursor.Site.AccountId == OwnAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
