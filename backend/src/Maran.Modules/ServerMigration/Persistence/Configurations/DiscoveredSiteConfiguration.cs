using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DiscoveredSite"/> onto <c>servermigration.DiscoveredSites</c>.</summary>
public sealed class DiscoveredSiteConfiguration : IEntityTypeConfiguration<DiscoveredSite>
{
    /// <summary>The widest an enum stored by name is.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>Configures the table, columns and the index on the owning account.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DiscoveredSite> builder)
    {
        builder.ToTable("DiscoveredSites");

        builder.HasKey(site => site.Id);
        builder.Property(site => site.Id).ValueGeneratedNever();

        builder.Property(site => site.Domain).IsRequired().HasMaxLength(DiscoveredSite.DomainMaxLength);
        builder.Property(site => site.AliasesText).IsRequired();
        builder.Property(site => site.Backend).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(site => site.PhpVersion).HasMaxLength(DiscoveredSite.PhpVersionMaxLength);
        builder.Property(site => site.DocumentRoot).IsRequired().HasMaxLength(DiscoveredSite.DocumentRootMaxLength);

        builder.HasIndex(site => site.DiscoveredAccountId).HasDatabaseName("IX_DiscoveredSites_DiscoveredAccountId");
    }
}
