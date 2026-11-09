using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteBasicAuthUser"/> onto the <c>sites.SiteBasicAuthUsers</c> table.</summary>
public sealed class SiteBasicAuthUserConfiguration : IEntityTypeConfiguration<SiteBasicAuthUser>
{
    /// <summary>Configures the table, key and column constraints.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteBasicAuthUser> builder)
    {
        builder.ToTable("SiteBasicAuthUsers");
        builder.HasKey(user => user.Id);

        // The application assigns the id, and says so: a Guid key EF believes the DATABASE generates, but
        // that arrives already set, is read as the id of a row that exists. A rule added to options that
        // are already tracked would then be saved as an UPDATE of nothing and fail, instead of as the
        // INSERT it is (options are replaced by adding rows to tracked options, not by Add on the set).
        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.SiteId)
            .IsRequired();

        // A row names its site as well as its set of options, and says so to the database: the same
        // column is also the key of the options (which the site cascades to), so an account deletion
        // that removes the site removes these rows by either path, and a model that names a site
        // without pointing at it is the shape the account-deletion census refuses. It adds no index,
        // because the one below already leads with the column.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(user => user.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(user => user.Position)
            .IsRequired();

        builder.Property(user => user.Name)
            .IsRequired()
            .HasMaxLength(32);

        // A bcrypt hash is 60 characters; the column leaves room for a stronger scheme without a
        // migration that widens it under a running release.
        builder.Property(user => user.PasswordHash)
            .IsRequired()
            .HasMaxLength(128);

        // A login name is unique within a site: two entries for one name would leave the credential
        // file with two lines for it, and the web server checks the first.
        builder.HasIndex(user => new { user.SiteId, user.Name })
            .IsUnique()
            .HasDatabaseName("IX_SiteBasicAuthUsers_SiteId_Name");
    }
}
