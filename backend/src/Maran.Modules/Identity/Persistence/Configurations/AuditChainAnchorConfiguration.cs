using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AuditChainAnchor"/> onto the <c>identity.AuditChainAnchors</c> table.
/// </summary>
public sealed class AuditChainAnchorConfiguration : IEntityTypeConfiguration<AuditChainAnchor>
{
    /// <summary>Configures the table, key, and column constraints for <see cref="AuditChainAnchor"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AuditChainAnchor> builder)
    {
        builder.ToTable("AuditChainAnchors");

        builder.HasKey(anchor => anchor.Id);

        // Never generated: the key is AuditChainAnchor.SingletonId, so there is exactly one anchor.
        builder.Property(anchor => anchor.Id).ValueGeneratedNever();

        builder.Property(anchor => anchor.PurgedThroughIndex).IsRequired();
        builder.Property(anchor => anchor.AnchorHash).IsRequired().HasColumnType("bytea");
        builder.Property(anchor => anchor.UpdatedAt).IsRequired();
    }
}
