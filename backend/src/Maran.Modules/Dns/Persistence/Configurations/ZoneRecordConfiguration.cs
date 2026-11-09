using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ZoneRecord"/> onto the <c>dns.ZoneRecords</c> table.</summary>
public sealed class ZoneRecordConfiguration : IEntityTypeConfiguration<ZoneRecord>
{
    /// <summary>The longest name of a record type or an origin, in characters.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>Configures the table, the cascade from the zone and the indexes for <see cref="ZoneRecord"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ZoneRecord> builder)
    {
        builder.ToTable("ZoneRecords");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();

        builder.Property(record => record.Name)
            .IsRequired()
            .HasMaxLength(DnsLimits.MaxNameLength);

        builder.Property(record => record.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(record => record.Ttl).IsRequired();

        builder.Property(record => record.Value)
            .IsRequired()
            .HasMaxLength(DnsLimits.MaxValueLength);

        builder.Property(record => record.Priority).IsRequired();
        builder.Property(record => record.Weight).IsRequired();
        builder.Property(record => record.Port).IsRequired();
        builder.Property(record => record.Flags).IsRequired();

        builder.Property(record => record.Tag)
            .IsRequired()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(record => record.Origin)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(record => record.CreatedAt).IsRequired();

        // Cascade: the records exist for exactly as long as the zone does.
        builder.HasOne(record => record.Zone)
            .WithMany(zone => zone.Records)
            .HasForeignKey(record => record.ZoneId)
            .OnDelete(DeleteBehavior.Cascade);

        // The natural key: a zone cannot hold the same record twice, whatever the concurrency. The domain
        // refuses a duplicate first with a typed answer; this index is the guarantee under it. The value is
        // capped at DnsLimits.MaxValueLength so the whole key stays far inside a PostgreSQL index row.
        builder.HasIndex(record => new
        {
            record.ZoneId,
            record.Name,
            record.Type,
            record.Value,
            record.Priority,
            record.Weight,
            record.Port,
            record.Flags,
            record.Tag,
        })
            .IsUnique()
            .HasDatabaseName("IX_ZoneRecords_NaturalKey");

        // "Which records sit at this name" is the read the CNAME-exclusivity rule makes.
        builder.HasIndex(record => new { record.ZoneId, record.Name })
            .HasDatabaseName("IX_ZoneRecords_ZoneId_Name");
    }
}
