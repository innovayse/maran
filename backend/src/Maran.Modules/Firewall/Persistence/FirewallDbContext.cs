using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Persistence.Configurations;

namespace Maran.Modules.Firewall.Persistence;

/// <summary>
/// The Firewall module's only database context. Owns the <c>firewall</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another module's
/// schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no tenant query filter here, and there must not be one.</b> Every other module's
/// context carries one because its rows belong to a customer; these rows belong to the SERVER. A
/// firewall rule, a ban and a whitelist entry are facts about the machine as a whole, the whole
/// surface is <c>[Authorize(AdminOnly)]</c>, and an administrator's scope is the server. A filter
/// closing over <c>ICurrentUser.AccountId</c> would have nothing to compare against and would
/// silently hide every row from the reconciler, which runs at startup with no request and no
/// principal at all — bans would then survive a reboot only while somebody happened to be signed in.
/// </para>
/// <para>
/// This context is therefore also the reason the module needs no <c>ICurrentUser</c>: the audit
/// journal reads the principal, and nothing about the DATA does.
/// </para>
/// </remarks>
public sealed class FirewallDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "firewall";

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    public FirewallDbContext(DbContextOptions<FirewallDbContext> options)
        : base(options)
    {
    }

    /// <summary>Every ban the panel has placed, whether or not it is still in force.</summary>
    /// <remarks>
    /// Expired and lifted episodes stay: they are what the escalation ladder counts, so removing
    /// them would reset every repeat offender to a fifteen-minute ban.
    /// </remarks>
    public DbSet<BanEpisode> BanEpisodes
    {
        get
        {
            return Set<BanEpisode>();
        }
    }

    /// <summary>The ranges the automatic bans never touch.</summary>
    public DbSet<WhitelistEntry> WhitelistEntries
    {
        get
        {
            return Set<WhitelistEntry>();
        }
    }

    /// <summary>The record that the installer's seed has been read, at most one row.</summary>
    /// <remarks>
    /// Separate from <see cref="WhitelistEntries"/> because it must survive the deletion of the row
    /// it created: the seeded exemption is deletable panel data, and the fact that it was seeded is
    /// not.
    /// </remarks>
    public DbSet<WhitelistSeedRecord> WhitelistSeedRecords
    {
        get
        {
            return Set<WhitelistSeedRecord>();
        }
    }

    /// <summary>Applies the schema and the entity configurations.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new BanEpisodeConfiguration());
        modelBuilder.ApplyConfiguration(new WhitelistEntryConfiguration());
        modelBuilder.ApplyConfiguration(new WhitelistSeedRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
