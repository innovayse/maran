using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="User"/> onto the <c>identity.Users</c> table.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Maximum stored length of the encrypted TOTP secret, which is longer than the plaintext.</summary>
    private const int EncryptedTotpSecretLength = 256;

    /// <summary>The cipher applied to <see cref="User.TotpSecret"/>.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration with the cipher its secret column needs.</summary>
    /// <param name="encryptionService">The cipher applied to the TOTP secret column.</param>
    public UserConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, keys, and column constraints for <see cref="User"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(u => u.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(UserState.Active);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.IsAccountSuspended)
            .IsRequired()
            .HasDefaultValue(false);

        // The TOTP secret is a second factor sitting at rest: anyone who reads this column can
        // generate the codes it protects. EncryptedStringConverter keeps the ciphertext in
        // PostgreSQL while the domain still reads plain text (rules/security.md item 8).
        // The `!` states what EF Core guarantees and the type system cannot: a value converter is
        // never handed a null. EncryptedStringConverter is declared over non-null strings — the
        // shape every other column wants — while this property is nullable because "no second
        // factor" is a real state, so the two disagree on paper and agree in practice.
        builder.Property(u => u.TotpSecret)
            .HasConversion(new EncryptedStringConverter(_encryptionService)!)
            .HasMaxLength(EncryptedTotpSecretLength);

        // Both are how a user is looked up at login, and both must identify exactly one person:
        // enforced by the database rather than only by the command, so a race cannot create twins.
        builder.HasIndex(u => u.Username).IsUnique().HasDatabaseName("IX_Users_Username");
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");

        // At most one login per hosting account: two would be two keys to the same account and
        // neither would be wrong, so the second is refused by the database rather than only by
        // AccountCreatedHandler's own pre-check, which a concurrent redelivery of AccountCreated can
        // race past. Partial, because PostgreSQL counts NULLs as distinct in a unique index and an
        // administrator's AccountId is NULL — several administrators, all with AccountId == null,
        // must stay legal.
        builder.HasIndex(u => u.AccountId)
            .IsUnique()
            .HasFilter("\"AccountId\" IS NOT NULL")
            .HasDatabaseName("UX_Users_AccountId");
    }
}
