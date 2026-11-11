using System.Diagnostics;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Persistence.Configurations;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Mail.Persistence;

/// <summary>
/// The Mail module's only database context. Owns the <c>mail</c> PostgreSQL schema exclusively — no other
/// module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>One unfiltered entity, and six that are.</b> <see cref="MailServerSettings"/> is a single row that
/// belongs to the server and to no customer; what authorises every path to it is the administrator-only
/// policy on its controller. Every other entity is a customer's row, and its query filter is not one
/// protection among several: it is THE authorisation mechanism of the customer-facing half of this
/// module. A handler that forgot a <c>Where</c> clause still cannot leak, and another tenant's resource
/// answers 404 because the row genuinely is not in the result set (rules/security.md item 6).
/// </para>
/// <para>
/// Mailboxes, aliases and domains carry an <c>AccountId</c>. A DKIM key, an alias target and an
/// autoresponder carry none and are scoped through their required parent, so a second copy of the
/// account that could disagree with the parent's does not exist.
/// </para>
/// </remarks>
public sealed class MailDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "mail";

    /// <summary>The authenticated principal whose tenant scope the rows are filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public MailDbContext(DbContextOptions<MailDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The installation's mail settings: at most one row.</summary>
    public DbSet<MailServerSettings> MailServerSettings
    {
        get
        {
            return Set<MailServerSettings>();
        }
    }

    /// <summary>The ledger of the local submission bindings the agent holds; server-wide, never returned to a request.</summary>
    public DbSet<MailSubmissionBinding> MailSubmissionBindings
    {
        get
        {
            return Set<MailSubmissionBinding>();
        }
    }

    /// <summary>The mail domains, already scoped to the current user's tenant.</summary>
    public DbSet<MailDomain> MailDomains
    {
        get
        {
            return Set<MailDomain>();
        }
    }

    /// <summary>The DKIM keys of the visible domains.</summary>
    public DbSet<DkimKey> DkimKeys
    {
        get
        {
            return Set<DkimKey>();
        }
    }

    /// <summary>The mailboxes, already scoped to the current user's tenant.</summary>
    public DbSet<Mailbox> Mailboxes
    {
        get
        {
            return Set<Mailbox>();
        }
    }

    /// <summary>The aliases, forwarders and catch-alls, already scoped to the current user's tenant.</summary>
    public DbSet<MailAlias> MailAliases
    {
        get
        {
            return Set<MailAlias>();
        }
    }

    /// <summary>The destinations of the visible aliases.</summary>
    public DbSet<MailAliasTarget> MailAliasTargets
    {
        get
        {
            return Set<MailAliasTarget>();
        }
    }

    /// <summary>The autoresponders of the visible mailboxes.</summary>
    public DbSet<MailAutoresponder> MailAutoresponders
    {
        get
        {
            return Set<MailAutoresponder>();
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
    /// The one account a customer acts for, or null for every other caller, none of whom own a row, so
    /// the filter shows them none through this arm.
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

    /// <summary>Applies the schema, every entity configuration, and the tenant query filters.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new MailServerSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new MailSubmissionBindingConfiguration());
        modelBuilder.ApplyConfiguration(new MailDomainConfiguration());
        modelBuilder.ApplyConfiguration(new DkimKeyConfiguration());
        modelBuilder.ApplyConfiguration(new MailboxConfiguration());
        modelBuilder.ApplyConfiguration(new MailAliasConfiguration());
        modelBuilder.ApplyConfiguration(new MailAliasTargetConfiguration());
        modelBuilder.ApplyConfiguration(new MailAutoresponderConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries their
        // account id and the filter closes over it.
        modelBuilder.Entity<MailDomain>().HasQueryFilter(domain =>
            SeesEveryAccount || domain.AccountId == OwnAccountId);
        modelBuilder.Entity<Mailbox>().HasQueryFilter(mailbox =>
            SeesEveryAccount || mailbox.AccountId == OwnAccountId);
        modelBuilder.Entity<MailAlias>().HasQueryFilter(alias =>
            SeesEveryAccount || alias.AccountId == OwnAccountId);

        // The children are scoped through their required parent and never by a second copy of the
        // account id: a child without its own filter would be a row a customer could read by id.
        modelBuilder.Entity<DkimKey>().HasQueryFilter(key =>
            SeesEveryAccount || key.MailDomain.AccountId == OwnAccountId);
        modelBuilder.Entity<MailAliasTarget>().HasQueryFilter(target =>
            SeesEveryAccount || target.MailAlias.AccountId == OwnAccountId);
        modelBuilder.Entity<MailAutoresponder>().HasQueryFilter(responder =>
            SeesEveryAccount || responder.Mailbox.AccountId == OwnAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
