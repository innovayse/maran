using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ZoneTemplateRecord"/> onto the <c>dns.ZoneTemplateRecords</c> table.</summary>
public sealed class ZoneTemplateRecordConfiguration : IEntityTypeConfiguration<ZoneTemplateRecord>
{
    /// <summary>The longest name of a record type, in characters.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>Configures the table, the cascade from the template and the columns for <see cref="ZoneTemplateRecord"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ZoneTemplateRecord> builder)
    {
        builder.ToTable("ZoneTemplateRecords");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();

        builder.Property(record => record.Position).IsRequired();

        // A template's name and value are held to the bounds of a zone record's own, so a template cannot
        // store what a zone could not.
        builder.Property(record => record.Name).IsRequired().HasMaxLength(DnsLimits.MaxNameLength);
        builder.Property(record => record.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);
        builder.Property(record => record.Ttl);
        builder.Property(record => record.Value).IsRequired().HasMaxLength(DnsLimits.MaxValueLength);
        builder.Property(record => record.Priority).IsRequired();
        builder.Property(record => record.Weight).IsRequired();
        builder.Property(record => record.Port).IsRequired();
        builder.Property(record => record.Flags).IsRequired();
        builder.Property(record => record.Tag).IsRequired().HasMaxLength(EnumNameMaxLength);

        // Cascade: the records exist for exactly as long as the template does.
        builder.HasOne<ZoneTemplate>()
            .WithMany(template => template.Records)
            .HasForeignKey(record => record.ZoneTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(record => new { record.ZoneTemplateId, record.Position })
            .HasDatabaseName("IX_ZoneTemplateRecords_ZoneTemplateId_Position");
    }
}
