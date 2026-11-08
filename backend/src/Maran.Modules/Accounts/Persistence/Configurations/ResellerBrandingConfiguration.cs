using Maran.Modules.Accounts.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Accounts.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ResellerBranding"/> onto the <c>accounts.ResellerBrandings</c> table.</summary>
public sealed class ResellerBrandingConfiguration : IEntityTypeConfiguration<ResellerBranding>
{
    /// <summary>Configures the table, the key and the cascade from the reseller for <see cref="ResellerBranding"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ResellerBranding> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("ResellerBrandings");
        builder.HasKey(b => b.ResellerId);

        builder.Property(b => b.DisplayName).HasMaxLength(ResellerBranding.DisplayNameMaxLength);

        // Seven characters: '#' and six hexadecimal digits.
        builder.Property(b => b.AccentColor).HasMaxLength(7);

        // The data URI of a small raster image: text, bounded by the entity's constant.
        builder.Property(b => b.Logo).HasMaxLength(ResellerBranding.LogoMaxLength);

        builder.Property(b => b.UpdatedAt).IsRequired();

        // A reseller that is deleted takes its branding with it; no handler has to remember to.
        builder.HasOne<Reseller>()
            .WithOne()
            .HasForeignKey<ResellerBranding>(b => b.ResellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
