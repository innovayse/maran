using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Persistence.Configurations;

namespace Maran.Modules.Idempotency.Persistence;

/// <summary>
/// The Idempotency module's only database context. Owns the <c>idempotency</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another module's
/// schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// It carries no tenant query filter and takes no <see cref="ICurrentUser"/>, deliberately: a record
/// is scoped by its <see cref="IdempotencyRecord.CallerKey"/>, which every lookup supplies, and not by
/// an account, so there is no principal for a filter to close over and the unattended purge needs no
/// filter bypass.
/// </remarks>
public sealed class IdempotencyDbContext : DbContext
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "idempotency";

    /// <summary>Creates the context.</summary>
    /// <param name="options">The provider and connection, from the module's registration.</param>
    public IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options)
        : base(options)
    {
    }

    /// <summary>The reservations and stored responses, one row per caller and key.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords
    {
        get
        {
            return Set<IdempotencyRecord>();
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
