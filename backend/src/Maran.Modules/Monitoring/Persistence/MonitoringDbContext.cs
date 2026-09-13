using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Persistence.Configurations;

namespace Maran.Modules.Monitoring.Persistence;

/// <summary>
/// The Monitoring module's only database context. Owns the <c>monitoring</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another module's
/// schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no tenant query filter here, and there must not be one.</b> Every row in this schema
/// describes the SERVER: one processor, one root filesystem, one set of managed
/// services. None of it belongs to a customer and none of it has an
/// <c>AccountId</c> to scope by, the whole surface is <c>[Authorize(AdminOnly)]</c>, and a filter
/// closing over <c>ICurrentUser.AccountId</c> would have nothing to compare against — while silently
/// hiding every row from the sampler and the retention pass, which run on a timer with no request
/// and no principal at all. That is the failure mode Firewall's context records for the same reason,
/// and it is worth stating twice: the charts would go blank whenever nobody was signed in.
/// </para>
/// <para>
/// <b>This schema holds no secret, and that is now structural.</b> Its one encrypted column was the
/// SMTP submission password, which left with the mailer for the <c>notifications</c> schema. So the
/// context needs no <c>IEncryptionService</c>, and a database dump of this schema taken for a
/// support case carries no credential for anything (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class MonitoringDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "monitoring";

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
        : base(options)
    {
    }

    /// <summary>Every raw reading of the host, for as long as the retention window keeps it.</summary>
    public DbSet<MetricSample> Samples
    {
        get
        {
            return Set<MetricSample>();
        }
    }

    /// <summary>What the panel currently believes about each monitored condition.</summary>
    public DbSet<AlertState> AlertStates
    {
        get
        {
            return Set<AlertState>();
        }
    }

    /// <summary>
    /// The shape the chart's bucketing SQL returns. A read model with no table behind it: the rows
    /// are computed by <c>date_bin</c> at read time and are never stored.
    /// </summary>
    public DbSet<MetricBucketRow> MetricBuckets
    {
        get
        {
            return Set<MetricBucketRow>();
        }
    }

    /// <summary>Applies the schema and the entity configurations.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new MetricSampleConfiguration());
        modelBuilder.ApplyConfiguration(new AlertStateConfiguration());
        modelBuilder.ApplyConfiguration(new MetricBucketRowConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
