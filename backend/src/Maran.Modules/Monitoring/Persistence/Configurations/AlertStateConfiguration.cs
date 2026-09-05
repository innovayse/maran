using Maran.Modules.Monitoring.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AlertState"/> onto the <c>monitoring.AlertStates</c> table.</summary>
public sealed class AlertStateConfiguration : IEntityTypeConfiguration<AlertState>
{
    /// <summary>The longest subject a row can name: a mount point or a managed service's name.</summary>
    private const int SubjectMaxLength = 200;

    /// <summary>Configures the table, key, and the uniqueness that makes deduplication possible.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AlertState> builder)
    {
        builder.ToTable("AlertStates");
        builder.HasKey(state => state.Id);

        builder.Property(state => state.Kind)
            .IsRequired();

        builder.Property(state => state.Subject)
            .IsRequired()
            .HasMaxLength(SubjectMaxLength);

        builder.Property(state => state.ConsecutiveBreaches)
            .IsRequired();

        builder.Property(state => state.IsFiring)
            .IsRequired();

        builder.Property(state => state.LastObservedAt)
            .IsRequired();

        // One row per condition, enforced by the database rather than by the evaluator remembering.
        // Two rows for one condition are two independent counters, and the alert would then be sent
        // twice — or, worse, raised by one row and resolved by the other, so the operator reads that
        // the disk recovered while it is still full.
        builder.HasIndex(state => new { state.Kind, state.Subject })
            .IsUnique()
            .HasDatabaseName("IX_AlertStates_Kind_Subject");
    }
}
