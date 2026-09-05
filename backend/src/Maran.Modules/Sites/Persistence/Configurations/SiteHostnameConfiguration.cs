using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteHostname"/> onto the <c>sites.SiteHostnames</c> table.</summary>
public sealed class SiteHostnameConfiguration : IEntityTypeConfiguration<SiteHostname>
{
    /// <summary>Configures the table, the exclusive key on the name, and the cascade from the site.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteHostname> builder)
    {
        builder.ToTable("SiteHostnames");

        // The NAME is the key, not a surrogate id with a unique index beside it: the row IS the
        // claim, and a table whose primary key is the claimed name cannot hold the same name twice
        // for any reason, in any account, under any concurrency (SiteHostname).
        builder.HasKey(hostname => hostname.Name);

        builder.Property(hostname => hostname.Name)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(hostname => hostname.SiteId)
            .IsRequired();

        // Cascade: the claims exist for exactly as long as the site does. Deleting a site must free
        // its names immediately, or a customer who deletes a site can never recreate it.
        builder.HasOne(hostname => hostname.Site)
            .WithMany(site => site.Hostnames)
            .HasForeignKey(hostname => hostname.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Which names does this site claim" is the read the delete path makes; without it that is
        // a scan of every hostname on the server.
        builder.HasIndex(hostname => hostname.SiteId).HasDatabaseName("IX_SiteHostnames_SiteId");
    }
}
