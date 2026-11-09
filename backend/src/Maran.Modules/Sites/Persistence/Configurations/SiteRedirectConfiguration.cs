using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteRedirect"/> onto the <c>sites.SiteRedirects</c> table.</summary>
public sealed class SiteRedirectConfiguration : IEntityTypeConfiguration<SiteRedirect>
{
    /// <summary>Configures the table, key and column constraints.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteRedirect> builder)
    {
        builder.ToTable("SiteRedirects");
        builder.HasKey(redirect => redirect.Id);

        // The application assigns the id, and says so: a Guid key EF believes the DATABASE generates, but
        // that arrives already set, is read as the id of a row that exists. A rule added to options that
        // are already tracked would then be saved as an UPDATE of nothing and fail, instead of as the
        // INSERT it is (options are replaced by adding rows to tracked options, not by Add on the set).
        builder.Property(redirect => redirect.Id)
            .ValueGeneratedNever();

        builder.Property(redirect => redirect.SiteId)
            .IsRequired();

        // A row names its site as well as its set of options, and says so to the database: the same
        // column is also the key of the options (which the site cascades to), so an account deletion
        // that removes the site removes these rows by either path, and a model that names a site
        // without pointing at it is the shape the account-deletion census refuses. It adds no index,
        // because the one below already leads with the column.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(redirect => redirect.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(redirect => redirect.Position)
            .IsRequired();

        builder.Property(redirect => redirect.Path)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(redirect => redirect.Match)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(redirect => redirect.Target)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(redirect => redirect.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // "Which rules does this site have, in order" is the only read there is.
        builder.HasIndex(redirect => new { redirect.SiteId, redirect.Position })
            .HasDatabaseName("IX_SiteRedirects_SiteId_Position");
    }
}
