using Maran.Modules.Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Marketplace.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="MarketplaceSettings"/> onto the <c>marketplace.MarketplaceSettings</c>
/// table.
/// </summary>
/// <remarks>
/// The primary key is what forbids a second row: every write addresses
/// <see cref="MarketplaceSettings.SingletonId"/>, so a duplicate is a key violation rather than a second
/// opinion about whether the marketplace is on.
/// </remarks>
public sealed class MarketplaceSettingsConfiguration : IEntityTypeConfiguration<MarketplaceSettings>
{
    /// <summary>Configures the table, key and columns for <see cref="MarketplaceSettings"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MarketplaceSettings> builder)
    {
        builder.ToTable("MarketplaceSettings");

        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.Enabled)
            .IsRequired();

        builder.Property(settings => settings.ChangedAt);

        builder.Property(settings => settings.ChangedBy);
    }
}
