using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteOptions"/> onto the <c>sites.SiteOptions</c> table.</summary>
public sealed class SiteOptionsConfiguration : IEntityTypeConfiguration<SiteOptions>
{
    /// <summary>Configures the table, the key, the child collections and their eager loading.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteOptions> builder)
    {
        // Both request-limit columns are set or both are null: a rate without a burst (or the reverse)
        // would be a half-limit no code path writes, and the check keeps it that way for a hand-written
        // row too.
        builder.ToTable(
            "SiteOptions",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SiteOptions_RequestRate",
                    "(\"RequestRatePerSecond\" IS NULL) = (\"RequestRateBurst\" IS NULL)");
            });

        // The site's id IS the key: a site has at most one set of options, and a table keyed on the
        // owner cannot hold a second. The relationship to the site is configured from the site's side
        // (SiteConfiguration), where the eager loading is also decided.
        builder.HasKey(options => options.SiteId);

        // The key is the site's own id, assigned by the application and never generated, so a set of
        // options first created on a tracked site is saved as an INSERT.
        builder.Property(options => options.SiteId)
            .ValueGeneratedNever();

        builder.Property(options => options.CanonicalHost)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(options => options.ClientMaxBodySizeMib);

        builder.Property(options => options.StaticCacheDays);

        builder.Property(options => options.IpAccessMode)
            .HasConversion<string>()
            .HasMaxLength(16);

        // The request limit is an owned value held in two nullable columns of this table. A set of
        // options with no limit has both null, and EF reads that back as no value at all, so "no limit"
        // is never a rate of zero.
        builder.OwnsOne(
            options => options.RequestRate,
            rate =>
            {
                rate.Property(value => value.PerSecond).HasColumnName("RequestRatePerSecond");
                rate.Property(value => value.Burst).HasColumnName("RequestRateBurst");
            });

        // The three child collections are loaded with their options, and the options with their site
        // (SiteConfiguration): a vhost is re-rendered from all of them or from none, so a read that
        // could leave one out is a read that could render a site without its credentials.
        builder.HasMany(options => options.Redirects)
            .WithOne(redirect => redirect.Options)
            .HasForeignKey(redirect => redirect.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(options => options.Redirects).AutoInclude();

        builder.HasMany(options => options.BasicAuthUsers)
            .WithOne(user => user.Options)
            .HasForeignKey(user => user.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(options => options.BasicAuthUsers).AutoInclude();

        builder.HasMany(options => options.IpRules)
            .WithOne(rule => rule.Options)
            .HasForeignKey(rule => rule.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(options => options.IpRules).AutoInclude();
    }
}
