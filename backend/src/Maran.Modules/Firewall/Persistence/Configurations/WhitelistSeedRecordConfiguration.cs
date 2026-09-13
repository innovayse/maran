using Maran.Modules.Firewall.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Firewall.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="WhitelistSeedRecord"/> onto the
/// <c>firewall.WhitelistSeedRecords</c> table.
/// </summary>
public sealed class WhitelistSeedRecordConfiguration : IEntityTypeConfiguration<WhitelistSeedRecord>
{
    /// <summary>The longest a CIDR range can be in text: an IPv6 address plus <c>/128</c>.</summary>
    private const int CidrMaxLength = 49;

    /// <summary>Configures the table and column constraints for <see cref="WhitelistSeedRecord"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<WhitelistSeedRecord> builder)
    {
        builder.ToTable("WhitelistSeedRecords");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Cidr)
            .IsRequired()
            .HasMaxLength(CidrMaxLength);

        builder.Property(record => record.SeededAt)
            .IsRequired();
    }
}
