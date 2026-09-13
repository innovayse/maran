using Maran.Modules.Ssl.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Ssl.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AcmeAccount"/> onto the <c>ssl.AcmeAccounts</c> table.</summary>
public sealed class AcmeAccountConfiguration : IEntityTypeConfiguration<AcmeAccount>
{
    /// <summary>The cipher the account key column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public AcmeAccountConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, keys, and column constraints for <see cref="AcmeAccount"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AcmeAccount> builder)
    {
        builder.ToTable("AcmeAccounts");
        builder.HasKey(account => account.Id);

        builder.Property(account => account.DirectoryUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(account => account.AccountUrl)
            .IsRequired()
            .HasMaxLength(2048);

        // Encrypted at rest through the panel's shared cipher, never plaintext (rules/security.md
        // item 8). The column is generous because ciphertext is longer than the PEM it protects.
        builder.Property(account => account.PrivateKeyPem)
            .IsRequired()
            .HasMaxLength(4096)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        builder.Property(account => account.CreatedAt)
            .IsRequired();

        // One registration per authority. Staging and production are different authorities, and a
        // second row for the same directory would be a second account nothing chooses between.
        builder.HasIndex(account => account.DirectoryUrl)
            .IsUnique()
            .HasDatabaseName("IX_AcmeAccounts_DirectoryUrl");
    }
}
