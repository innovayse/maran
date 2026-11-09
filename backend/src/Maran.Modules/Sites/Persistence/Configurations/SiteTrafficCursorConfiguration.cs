using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteTrafficCursor"/> onto the <c>sites.SiteTrafficCursors</c> table.</summary>
public sealed class SiteTrafficCursorConfiguration : IEntityTypeConfiguration<SiteTrafficCursor>
{
    /// <summary>Configures the table, the key on the site, and the cascade from the site.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteTrafficCursor> builder)
    {
        builder.ToTable("SiteTrafficCursors");

        // The site IS the key: a site has one cursor, and a table keyed by it cannot hold two.
        builder.HasKey(cursor => cursor.SiteId);

        builder.Property(cursor => cursor.FileId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(cursor => cursor.Offset)
            .IsRequired();

        builder.Property(cursor => cursor.UpdatedAt)
            .IsRequired();

        // Nullable: a site whose log was never found incomplete has no such instant.
        builder.Property(cursor => cursor.IncompleteSince);

        // Cascade: a deleted site takes its cursor with it.
        builder.HasOne(cursor => cursor.Site)
            .WithMany()
            .HasForeignKey(cursor => cursor.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
