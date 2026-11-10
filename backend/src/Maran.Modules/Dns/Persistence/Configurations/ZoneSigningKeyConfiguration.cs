using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ZoneSigningKey"/> onto the <c>dns.ZoneSigningKeys</c> table.</summary>
public sealed class ZoneSigningKeyConfiguration : IEntityTypeConfiguration<ZoneSigningKey>
{
    /// <summary>The longest public key column: 64 bytes in base64 is 88 characters.</summary>
    private const int PublicKeyMaxLength = 128;

    /// <summary>The longest private key column: the cipher's nonce, tag and 44-character scalar in base64 stay far inside it.</summary>
    private const int PrivateKeyMaxLength = 256;

    /// <summary>The cipher the private key column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public ZoneSigningKeyConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, the cascade from the zone, the encrypted column and the index for <see cref="ZoneSigningKey"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ZoneSigningKey> builder)
    {
        builder.ToTable("ZoneSigningKeys");

        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.PublicKey)
            .IsRequired()
            .HasMaxLength(PublicKeyMaxLength);

        // Encrypted at rest through the panel's shared cipher, never plaintext (rules/security.md item 8).
        // The column is generous because ciphertext is longer than the scalar it protects.
        builder.Property(key => key.PrivateKey)
            .IsRequired()
            .HasMaxLength(PrivateKeyMaxLength)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        builder.Property(key => key.Flags).IsRequired();
        builder.Property(key => key.Algorithm).IsRequired();
        builder.Property(key => key.KeyTag).IsRequired();
        builder.Property(key => key.CreatedAt).IsRequired();

        // Cascade: the key exists for exactly as long as the zone does.
        builder.HasOne(key => key.Zone)
            .WithMany(zone => zone.SigningKeys)
            .HasForeignKey(key => key.ZoneId)
            .OnDelete(DeleteBehavior.Cascade);

        // One key per zone in this release (no automatic rollover): the guarantee under the zone's own methods.
        builder.HasIndex(key => key.ZoneId)
            .IsUnique()
            .HasDatabaseName("IX_ZoneSigningKeys_ZoneId");
    }
}
