using Maran.Modules.Monitoring.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AlertThresholds"/> onto <c>monitoring.AlertThresholds</c>, one row
/// of which is the whole table.
/// </summary>
public sealed class AlertThresholdsConfiguration : IEntityTypeConfiguration<AlertThresholds>
{
    /// <summary>Configures the table, key, columns and the bounds the database holds the entity to.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AlertThresholds> builder)
    {
        builder.ToTable("AlertThresholds", table =>
        {
            // The entity's bounds, restated where a row written some other way would be caught.
            table.HasCheckConstraint(
                "CK_AlertThresholds_DiskPercent",
                $"\"DiskPercent\" BETWEEN {AlertThresholds.MinimumPercent} AND {AlertThresholds.MaximumPercent}");
            table.HasCheckConstraint(
                "CK_AlertThresholds_CpuPercent",
                $"\"CpuPercent\" IS NULL OR \"CpuPercent\" BETWEEN {AlertThresholds.MinimumPercent} AND {AlertThresholds.MaximumPercent}");
            table.HasCheckConstraint(
                "CK_AlertThresholds_MemoryPercent",
                $"\"MemoryPercent\" IS NULL OR \"MemoryPercent\" BETWEEN {AlertThresholds.MinimumPercent} AND {AlertThresholds.MaximumPercent}");
        });
        builder.HasKey(thresholds => thresholds.Id);

        // Never generated: the key is the constant AlertThresholds.SingletonId, which is what makes a
        // second row impossible rather than merely unusual.
        builder.Property(thresholds => thresholds.Id)
            .ValueGeneratedNever();

        builder.Property(thresholds => thresholds.DiskPercent)
            .IsRequired();

        // Nullable on purpose: null is "not watched", the state of a panel upgraded from a release
        // that never watched the processor or the memory.
        builder.Property(thresholds => thresholds.CpuPercent);

        builder.Property(thresholds => thresholds.MemoryPercent);

        builder.Property(thresholds => thresholds.UpdatedAt)
            .IsRequired();
    }
}
