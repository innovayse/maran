using Maran.Modules.Monitoring.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MetricSample"/> onto the <c>monitoring.Samples</c> table.</summary>
public sealed class MetricSampleConfiguration : IEntityTypeConfiguration<MetricSample>
{
    /// <summary>Configures the table, key, and the one index every read of this table uses.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MetricSample> builder)
    {
        builder.ToTable("Samples");
        builder.HasKey(sample => sample.Id);

        builder.Property(sample => sample.Id)
            .ValueGeneratedOnAdd();

        builder.Property(sample => sample.CapturedAt)
            .IsRequired();

        // Every read of this table is a time range: the chart asks for the last day or the last
        // week, the retention pass asks for everything older than seven days, and the alert
        // evaluator asks for the newest row. Without this index each of them is a sequential scan
        // of the whole window — which is small (R10 sizes it at about ten thousand rows) but is
        // scanned on every chart refresh of every admin session.
        builder.HasIndex(sample => sample.CapturedAt)
            .HasDatabaseName("IX_Samples_CapturedAt");
    }
}
