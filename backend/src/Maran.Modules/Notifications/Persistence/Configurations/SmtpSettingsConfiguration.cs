using Maran.Modules.Notifications.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SmtpSettings"/> onto the <c>monitoring.SmtpSettings</c> table, one
/// row of which is the whole table.
/// </summary>
public sealed class SmtpSettingsConfiguration : IEntityTypeConfiguration<SmtpSettings>
{
    /// <summary>The longest host name the standard allows.</summary>
    private const int HostMaxLength = 255;

    /// <summary>The longest an address or a submission user name may be.</summary>
    private const int AddressMaxLength = 320;

    /// <summary>The longest display name that may sit beside the sender address.</summary>
    private const int DisplayNameMaxLength = 200;

    /// <summary>
    /// The longest ciphertext the password column holds. Generous relative to any real password
    /// because the stored value is AES-GCM ciphertext plus its nonce and tag, base64-encoded — the
    /// column is sized for what is written, not for what is typed.
    /// </summary>
    private const int EncryptedPasswordMaxLength = 1024;

    /// <summary>The cipher applied to the password column at rest.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration with the cipher the password column goes through.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public SmtpSettingsConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, key, and column constraints for <see cref="SmtpSettings"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SmtpSettings> builder)
    {
        builder.ToTable("SmtpSettings");
        builder.HasKey(settings => settings.Id);

        // Never generated. The key is the constant SmtpSettings.SingletonId, which is what makes a
        // second row impossible rather than merely unusual.
        builder.Property(settings => settings.Id)
            .ValueGeneratedNever();

        builder.Property(settings => settings.Host)
            .IsRequired()
            .HasMaxLength(HostMaxLength);

        builder.Property(settings => settings.Port)
            .IsRequired();

        builder.Property(settings => settings.Security)
            .IsRequired();

        builder.Property(settings => settings.Username)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        // The one encrypted column in this schema. PostgreSQL therefore only ever holds ciphertext,
        // and a database dump taken for a support case carries no working credential for the
        // operator's mail provider (rules/security.md item 8).
        builder.Property(settings => settings.Password)
            .IsRequired()
            .HasMaxLength(EncryptedPasswordMaxLength)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        builder.Property(settings => settings.FromAddress)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(settings => settings.FromName)
            .IsRequired()
            .HasMaxLength(DisplayNameMaxLength);

        builder.Property(settings => settings.AlertRecipient)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(settings => settings.UpdatedAt)
            .IsRequired();
    }
}
