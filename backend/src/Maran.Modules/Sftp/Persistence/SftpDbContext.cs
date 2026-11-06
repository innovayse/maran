using System.Diagnostics;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence.Configurations;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Sftp.Persistence;

/// <summary>
/// The Sftp module's only database context. Owns the <c>sftp</c> PostgreSQL schema exclusively — no
/// other module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// The tenant filter below is not one protection among several here: it is THE authorisation
/// mechanism of this module. The host has no notion of a tenant, so nothing the agent can be asked
/// answers "may this customer see, delete or re-credential this login" — the panel's own rows are
/// the only record of who asked for what, and this filter is what makes reading them safe. A handler
/// that forgot a <c>Where</c> clause still cannot leak, and another tenant's login answers 404
/// rather than 403 because the row genuinely is not in the result set.
/// </remarks>
public sealed class SftpDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "sftp";

    /// <summary>The authenticated principal whose tenant scope every query is filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SftpDbContext(DbContextOptions<SftpDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The SFTP logins owned by this module, already scoped to the current user's tenant.</summary>
    public DbSet<SftpUser> SftpUsers
    {
        get
        {
            return Set<SftpUser>();
        }
    }

    /// <summary>The public keys of those logins, scoped to the tenant through the owning login.</summary>
    public DbSet<SftpUserKey> SftpUserKeys
    {
        get
        {
            return Set<SftpUserKey>();
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

    /// <summary>Applies the schema, the entity configuration, and the tenant query filter.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new SftpUserConfiguration());
        modelBuilder.ApplyConfiguration(new SftpUserKeyConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler
        // remembering to filter. An administrator sees everything; a customer's context carries
        // their account id and the filter closes over it.
        modelBuilder.Entity<SftpUser>().HasQueryFilter(sftpUser =>
            SeesEveryAccount || sftpUser.AccountId == OwnAccountId);

        // A key belongs to whoever owns the login it opens, so its scope is read through the required
        // relationship rather than from a second copy of the account id that could disagree with the
        // login's own (rules/security.md item 6: every tenant entity is filtered).
        modelBuilder.Entity<SftpUserKey>().HasQueryFilter(key =>
            SeesEveryAccount || key.SftpUser.AccountId == OwnAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
