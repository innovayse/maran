using Maran.Modules.Firewall.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Firewall.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="WhitelistEntry"/> onto the <c>firewall.WhitelistEntries</c> table.
/// </summary>
public sealed class WhitelistEntryConfiguration : IEntityTypeConfiguration<WhitelistEntry>
{
    /// <summary>The longest a CIDR range can be in text: an IPv6 address plus <c>/128</c>.</summary>
    private const int CidrMaxLength = 49;

    /// <summary>The longest note an administrator may leave beside a range.</summary>
    private const int NoteMaxLength = 200;

    /// <summary>Configures the table, keys, and column constraints for <see cref="WhitelistEntry"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<WhitelistEntry> builder)
    {
        builder.ToTable("WhitelistEntries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Cidr)
            .IsRequired()
            .HasMaxLength(CidrMaxLength);

        builder.Property(entry => entry.Note)
            .IsRequired()
            .HasMaxLength(NoteMaxLength);

        builder.Property(entry => entry.CreatedAt)
            .IsRequired();

        // One row per range. Two rows for the same range are not merely untidy: removing one would
        // leave the exemption in place while the screen said it had gone, which is the state an
        // operator would discover by being banned.
        builder.HasIndex(entry => entry.Cidr)
            .IsUnique()
            .HasDatabaseName("IX_WhitelistEntries_Cidr");
    }
}
