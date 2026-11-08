using System.Diagnostics;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Persistence.Configurations;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Dns.Persistence;

/// <summary>
/// The Dns module's only database context. Owns the <c>dns</c> PostgreSQL schema exclusively — no other
/// module reads or writes it, and this module never reads another module's schema
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>Three entities, and exactly one of them is not filtered.</b> <see cref="DnsSettings"/> is a single
/// installation-wide row that belongs to the server and to no customer; what authorises every path to it is
/// the <c>AdminOnly</c> policy on <c>DnsServerController</c>.
/// </para>
/// <para>
/// <see cref="Zones"/> and <see cref="ZoneRecords"/> are the customers' rows, and their filter is THE
/// authorisation mechanism of the customer-facing half of this module: the name server has no notion of a
/// tenant, so the panel's rows are the only record of who owns which name. The filter FAILS CLOSED. An
/// administrator sees every zone; a customer sees the zones of the one account they act for; a reseller, an
/// API key, an anonymous caller and a login with no account selected see none. A reseller manages accounts,
/// not their names, and a machine caller has no DNS scope. A zone another account owns answers 404 because
/// the row genuinely is not in the result set (rules/security.md item 6). A record is reached through its
/// zone, so it carries the zone's filter and no account id of its own.
/// </para>
/// </remarks>
public sealed class DnsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "dns";

    /// <summary>The authenticated principal whose tenant scope the zone rows are filtered by.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public DnsDbContext(DbContextOptions<DnsDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>The zones this module owns, already scoped to the current user's tenant.</summary>
    public DbSet<Zone> Zones
    {
        get
        {
            return Set<Zone>();
        }
    }

    /// <summary>The records of those zones, scoped through their zone.</summary>
    public DbSet<ZoneRecord> ZoneRecords
    {
        get
        {
            return Set<ZoneRecord>();
        }
    }

    /// <summary>The installation's DNS settings: at most one row, keyed by a fixed identity.</summary>
    public DbSet<DnsSettings> DnsSettings
    {
        get
        {
            return Set<DnsSettings>();
        }
    }

    /// <summary>
    /// True when the caller is an administrator, who sees every account's zones. Read per context instance
    /// by the tenant filter, which an expression tree cannot express as a type switch.
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
    /// The one account a customer acts for, or null for every other caller, none of whom own a zone, so the
    /// filter shows them none through this arm.
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

    /// <summary>Applies the schema, the entity configurations and the tenant query filters.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new DnsSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneRecordConfiguration());

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not by a handler remembering
        // to filter. The record's filter is its zone's, spelled out because EF warns when a dependent is
        // filtered differently from its principal.
        modelBuilder.Entity<Zone>().HasQueryFilter(zone =>
            SeesEveryAccount || zone.AccountId == OwnAccountId);
        modelBuilder.Entity<ZoneRecord>().HasQueryFilter(record =>
            SeesEveryAccount || record.Zone.AccountId == OwnAccountId);

        base.OnModelCreating(modelBuilder);
    }
}
