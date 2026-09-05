using Maran.Modules.Firewall.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Firewall.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="BanEpisode"/> onto the <c>firewall.BanEpisodes</c> table.</summary>
public sealed class BanEpisodeConfiguration : IEntityTypeConfiguration<BanEpisode>
{
    /// <summary>The longest an address can be in text: an IPv6 address with an embedded IPv4 tail.</summary>
    private const int AddressMaxLength = 45;

    /// <summary>Configures the table, keys, and column constraints for <see cref="BanEpisode"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<BanEpisode> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("BanEpisodes");
        builder.HasKey(episode => episode.Id);

        builder.Property(episode => episode.IpAddress)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(episode => episode.Reason)
            .IsRequired();

        builder.Property(episode => episode.Failures)
            .IsRequired();

        builder.Property(episode => episode.BannedAt)
            .IsRequired();

        // The idempotency key. A detector's message can be delivered twice — a durable queue
        // behaving correctly — and the second delivery must not extend the ban or count as a second
        // offence on the escalation ladder. WindowStart is null for a manual ban, and PostgreSQL
        // treats nulls in a unique index as distinct, which is exactly the rule wanted: an
        // administrator banning the same address twice IS two decisions.
        builder.HasIndex(episode => new { episode.IpAddress, episode.WindowStart })
            .IsUnique()
            .HasDatabaseName("IX_BanEpisodes_IpAddress_WindowStart");

        // The reconciler's query at every startup ("what is still in force"), and the escalation
        // ladder's ("how often has this address been banned today").
        builder.HasIndex(episode => new { episode.IpAddress, episode.BannedAt })
            .HasDatabaseName("IX_BanEpisodes_IpAddress_BannedAt");

        builder.HasIndex(episode => episode.ExpiresAt)
            .HasDatabaseName("IX_BanEpisodes_ExpiresAt");
    }
}
