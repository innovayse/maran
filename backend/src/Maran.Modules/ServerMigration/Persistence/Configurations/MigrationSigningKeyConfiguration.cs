using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MigrationSigningKey"/> onto <c>servermigration.MigrationSigningKeys</c>.</summary>
/// <remarks>The private key is encrypted at rest through the panel's cipher; the column is unbounded text because ciphertext is longer than the document it protects.</remarks>
public sealed class MigrationSigningKeyConfiguration : IEntityTypeConfiguration<MigrationSigningKey>
{
    /// <summary>The cipher the private key column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public MigrationSigningKeyConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table and columns.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MigrationSigningKey> builder)
    {
        builder.ToTable("MigrationSigningKeys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.PrivateKey).IsRequired().HasConversion(new EncryptedStringConverter(_encryptionService));
        builder.Property(key => key.PublicKey).IsRequired().HasMaxLength(MigrationSigningKey.PublicKeyMaxLength);
        builder.Property(key => key.Fingerprint).IsRequired().HasMaxLength(MigrationSigningKey.FingerprintLength);
        builder.Property(key => key.CreatedAt).IsRequired();
    }
}
