using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="FailedLoginByIp"/> onto the <c>identity.FailedLoginByIp</c> table.
/// </summary>
public sealed class FailedLoginByIpConfiguration : IEntityTypeConfiguration<FailedLoginByIp>
{
    /// <summary>Longest textual address the panel stores, matching every other address column.</summary>
    private const int AddressLength = 45;

    /// <summary>Configures the table, key, and column constraints for <see cref="FailedLoginByIp"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<FailedLoginByIp> builder)
    {
        builder.ToTable("FailedLoginByIp");

        // The address IS the key, not a surrogate beside a unique index. There is exactly one open
        // window per address, and making that a primary key is what makes a duplicate impossible
        // rather than merely unlikely: two rows for one address would each count half the attempts
        // and neither would ever reach the threshold.
        builder.HasKey(f => f.IpAddress);

        builder.Property(f => f.IpAddress)
            .IsRequired()
            .HasMaxLength(AddressLength);

        builder.Property(f => f.WindowStart)
            .IsRequired();

        builder.Property(f => f.Failures)
            .IsRequired();

        // Every refused sign-in reclaims a batch of windows that have already closed, and that
        // sweep is a range scan on this column. Without the index it is a full scan of the table on
        // the one code path that runs most often while the panel is under attack — the sweep that
        // exists to keep the table small would itself be what a login flood costs.
        builder.HasIndex(f => f.WindowStart).HasDatabaseName("IX_FailedLoginByIp_WindowStart");
    }
}
