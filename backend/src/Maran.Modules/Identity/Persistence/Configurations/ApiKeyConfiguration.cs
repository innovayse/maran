using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ApiKey"/> onto the <c>identity.ApiKeys</c> table.</summary>
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="ApiKey"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name)
            .IsRequired()
            .HasMaxLength(ApiKeyLimits.MaxNameLength);

        builder.Property(k => k.Prefix)
            .IsRequired()
            .HasMaxLength(ApiKeyLimits.PrefixLength);

        builder.Property(k => k.TokenHash)
            .IsRequired()
            .HasMaxLength(ApiKeyLimits.MaxDigestLength);

        // A closed list of scope names in a native array column: the vocabulary is code, so a row
        // naming a scope this build no longer knows is read as granting nothing, not as an error.
        builder.Property(k => k.Scopes)
            .IsRequired()
            .HasColumnType("text[]");

        builder.Property(k => k.AllowedCidrs)
            .HasColumnType("text[]");

        builder.Property(k => k.CreatedByUserId)
            .IsRequired();

        // The owning reseller, or null for a key the administrator owns. Deliberately not a foreign
        // key: the reseller lives in another module's schema, and its deletion removes its keys through
        // an event. Indexed because every reseller-side list and count asks for "this owner's keys".
        builder.Property(k => k.ResellerId);

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.Property(k => k.ExpiresAt);

        builder.Property(k => k.LastUsedAt);

        builder.Property(k => k.LastUsedIp)
            .HasMaxLength(ApiKeyLimits.MaxAddressLength);

        builder.Property(k => k.RevokedAt);

        // The lookup of every authenticated call, and what keeps two keys from sharing a handle.
        builder.HasIndex(k => k.Prefix)
            .IsUnique()
            .HasDatabaseName("UX_ApiKeys_Prefix");

        builder.HasIndex(k => k.ResellerId)
            .HasDatabaseName("IX_ApiKeys_ResellerId");
    }
}
