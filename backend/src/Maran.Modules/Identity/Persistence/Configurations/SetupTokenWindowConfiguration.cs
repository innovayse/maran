using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SetupTokenWindow"/> onto the <c>identity.SetupTokenWindow</c>
/// table.
/// </summary>
/// <remarks>
/// Read the column list for what is not here: there is no token column. The fingerprint is the only
/// thing about the token this table holds, and that is the design (see
/// <see cref="SetupTokenWindow"/>).
/// </remarks>
public sealed class SetupTokenWindowConfiguration : IEntityTypeConfiguration<SetupTokenWindow>
{
    /// <summary>Length of a lowercase hex SHA-256 digest.</summary>
    private const int FingerprintLength = 64;

    /// <summary>Configures the table, key, and column constraints for <see cref="SetupTokenWindow"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SetupTokenWindow> builder)
    {
        // Singular, because the table holds one row by construction: the key is a constant nothing
        // generates a second value for.
        builder.ToTable("SetupTokenWindow");

        builder.HasKey(window => window.Id);

        // Never generated: the entity sets SetupTokenWindow.SingletonId and that value must reach the
        // database, or every lookup by the constant misses a row that was written under another id.
        builder.Property(window => window.Id).ValueGeneratedNever();

        builder.Property(window => window.TokenFingerprint)
            .IsRequired()
            .HasMaxLength(FingerprintLength);

        builder.Property(window => window.OpenedAt).IsRequired();
    }
}
