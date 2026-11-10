using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ExportGrant"/> onto <c>servermigration.ExportGrants</c>.</summary>
/// <remarks>The digest is unique, which is what makes a lookup by presented token unambiguous; the token itself is never a column.</remarks>
public sealed class ExportGrantConfiguration : IEntityTypeConfiguration<ExportGrant>
{
    /// <summary>Configures the table, columns, the cascade from the export and the unique digest.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ExportGrant> builder)
    {
        builder.ToTable("ExportGrants");
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id).ValueGeneratedNever();
        builder.Property(grant => grant.ExportId).IsRequired();
        builder.Property(grant => grant.TokenDigest).IsRequired().HasMaxLength(ExportGrant.DigestLength);
        builder.Property(grant => grant.AllowedFrom).HasMaxLength(ExportGrant.AllowedFromMaxLength);
        builder.Property(grant => grant.ExpiresAt).IsRequired();
        builder.Property(grant => grant.ManifestReads).IsRequired();
        builder.Property(grant => grant.ArtifactConsumedAt);
        builder.Property(grant => grant.RevokedAt);
        builder.Property(grant => grant.CreatedAt).IsRequired();

        builder.HasOne<AccountExport>()
            .WithMany()
            .HasForeignKey(grant => grant.ExportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(grant => grant.TokenDigest).IsUnique().HasDatabaseName("IX_ExportGrants_TokenDigest");
        builder.HasIndex(grant => grant.ExportId).HasDatabaseName("IX_ExportGrants_ExportId");
    }
}
