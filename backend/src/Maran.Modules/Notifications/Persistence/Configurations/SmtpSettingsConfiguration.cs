using Maran.Modules.Notifications.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SmtpSettings"/> onto the <c>notifications.SmtpSettings</c> table, one
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

        builder.Property(settings => settings.AuthenticationMode)
            .IsRequired();

        // Present only in basic mode: null in the anonymous mode, which the check constraint below
        // enforces, so "no credentials" is never an empty string a reader has to know to test for.
        builder.Property(settings => settings.BasicUsername)
            .HasMaxLength(AddressMaxLength);

        // Encrypted exactly as the legacy Password column is, with the same converter, so the
        // backfill below can copy ciphertext between the two without ever decrypting it. A null
        // never reaches the converter: EF stores it as NULL.
        builder.Property(settings => settings.BasicPassword)
            .HasMaxLength(EncryptedPasswordMaxLength)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        // Expand phase of the contract: the three legacy columns the previous release reads stay
        // mapped (to private properties the entity rewrites on every change) until a later release
        // drops them. The converter on the password is unchanged, so the previous release decrypts it.
        builder.Property<string>("LegacyUsername")
            .HasColumnName("Username")
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        // The one encrypted column in this schema before the typed one above. PostgreSQL therefore
        // only ever holds ciphertext, and a database dump taken for a support case carries no
        // working credential for the operator's mail provider (rules/security.md item 8).
        builder.Property<string>("LegacyPassword")
            .HasColumnName("Password")
            .IsRequired()
            .HasMaxLength(EncryptedPasswordMaxLength)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        builder.Property<string>("LegacyAlertRecipient")
            .HasColumnName("AlertRecipient")
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(settings => settings.FromAddress)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(settings => settings.FromName)
            .IsRequired()
            .HasMaxLength(DisplayNameMaxLength);

        builder.Property(settings => settings.AlertRecipientAddress)
            .HasMaxLength(AddressMaxLength);

        builder.Property(settings => settings.UpdatedAt)
            .IsRequired();

        // The typed reading of AuthenticationMode and the two basic-mode columns; computed, never stored.
        builder.Ignore(settings => settings.Credentials);

        // A user name and password exist exactly when the mode is basic. AuthenticationMode 0 is
        // Anonymous and 1 is Basic (SmtpAuthentication), written as the numbers the column holds.
        builder.ToTable("SmtpSettings", table => table.HasCheckConstraint(
            "CK_SmtpSettings_BasicCredentialsOnlyInBasicMode",
            "(\"AuthenticationMode\" = 0 AND \"BasicUsername\" IS NULL AND \"BasicPassword\" IS NULL) "
            + "OR (\"AuthenticationMode\" = 1 AND \"BasicUsername\" IS NOT NULL AND \"BasicPassword\" IS NOT NULL)"));

        // BACKFILL (expand phase), to be placed verbatim in the Up method of the migration that adds
        // AuthenticationMode, BasicUsername, BasicPassword and AlertRecipientAddress, AFTER the columns
        // exist and BEFORE the check constraint is created. Down needs no data step: dropping the
        // four columns leaves the legacy ones, which this release kept in step, untouched.
        //
        // raw-sql: copies the legacy columns into the typed ones; no parameters, no caller input.
        //   UPDATE notifications."SmtpSettings"
        //   SET "AuthenticationMode" = CASE WHEN "Username" <> '' THEN 1 ELSE 0 END,
        //       "BasicUsername"      = CASE WHEN "Username" <> '' THEN "Username" ELSE NULL END,
        //       "BasicPassword"      = CASE WHEN "Username" <> '' THEN "Password" ELSE NULL END,
        //       "AlertRecipientAddress" = NULLIF(btrim("AlertRecipient"), '');
        //
        // "Password" is copied as stored (ciphertext): both columns use the same converter and key.
        // A row whose user name was non-empty keeps its password, an empty ciphertext included; a
        // row with no user name becomes anonymous and its password, which no sender read, is dropped.
    }
}
