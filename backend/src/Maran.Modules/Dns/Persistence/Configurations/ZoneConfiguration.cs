using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Zone"/> onto the <c>dns.Zones</c> table.</summary>
public sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    /// <summary>Configures the table, keys, concurrency token and indexes for <see cref="Zone"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("Zones");

        // Assigned by the application and never generated, so a zone first created on a tracked context
        // is saved as an INSERT and a record added through the zone is not mistaken for an existing row.
        builder.HasKey(zone => zone.Id);
        builder.Property(zone => zone.Id).ValueGeneratedNever();

        builder.Property(zone => zone.AccountId).IsRequired();

        builder.Property(zone => zone.Name)
            .IsRequired()
            .HasMaxLength(HostNameRule.MaximumLength);

        // The serial is the optimistic-concurrency token: every change moves it, so two overlapping edits
        // of one zone cannot both be saved. The panel serialises edits with an advisory lock first; this is
        // the second guard, for the day a path forgets the first.
        builder.Property(zone => zone.Serial)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(zone => zone.CreatedAt).IsRequired();

        // The DNSSEC step the zone is in, stored as its number with a database default of zero (off), so the
        // rows that exist before signing do and a release that does not know the column keeps inserting zones.
        builder.Property(zone => zone.DnssecState).IsRequired();

        // Set only while the zone is being withdrawn from signing: the start of the minimum wait.
        builder.Property(zone => zone.DnssecWithdrawalStartedAt);
        builder.Property(zone => zone.UpdatedAt).IsRequired();

        // One owner per name across the WHOLE server. Names are stored lower case, so this index is also the
        // case-insensitive uniqueness. It refuses the same name; the parent and child collisions the daemon's
        // most-specific-zone rule would turn into a hijack are refused by ZoneOverlapPolicy.
        builder.HasIndex(zone => zone.Name)
            .IsUnique()
            .HasDatabaseName("IX_Zones_Name");

        // Every tenant-scoped read is "this account's zones": the query the global filter emits on every
        // request from a customer, and the read the plan-limit count makes on every creation.
        builder.HasIndex(zone => zone.AccountId).HasDatabaseName("IX_Zones_AccountId");
    }
}
