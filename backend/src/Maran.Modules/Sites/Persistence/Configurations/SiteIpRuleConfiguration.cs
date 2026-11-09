using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteIpRule"/> onto the <c>sites.SiteIpRules</c> table.</summary>
public sealed class SiteIpRuleConfiguration : IEntityTypeConfiguration<SiteIpRule>
{
    /// <summary>Configures the table, key and column constraints.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteIpRule> builder)
    {
        builder.ToTable("SiteIpRules");
        builder.HasKey(rule => rule.Id);

        // The application assigns the id, and says so: a Guid key EF believes the DATABASE generates, but
        // that arrives already set, is read as the id of a row that exists. A rule added to options that
        // are already tracked would then be saved as an UPDATE of nothing and fail, instead of as the
        // INSERT it is (options are replaced by adding rows to tracked options, not by Add on the set).
        builder.Property(rule => rule.Id)
            .ValueGeneratedNever();

        builder.Property(rule => rule.SiteId)
            .IsRequired();

        // A row names its site as well as its set of options, and says so to the database: the same
        // column is also the key of the options (which the site cascades to), so an account deletion
        // that removes the site removes these rows by either path, and a model that names a site
        // without pointing at it is the shape the account-deletion census refuses. It adds no index,
        // because the one below already leads with the column.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(rule => rule.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(rule => rule.Position)
            .IsRequired();

        // The longest IPv6 range in text form is 43 characters ("address/128").
        builder.Property(rule => rule.Cidr)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(rule => new { rule.SiteId, rule.Position })
            .HasDatabaseName("IX_SiteIpRules_SiteId_Position");
    }
}
